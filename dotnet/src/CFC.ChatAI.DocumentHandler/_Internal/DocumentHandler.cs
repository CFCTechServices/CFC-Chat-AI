using System.Text.Json;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Storage;
using CFC.ChatAI.VideoProcessor;

namespace CFC.ChatAI.DocumentHandler._Internal;

/// <summary>Coordinates independent processors and provider contracts without moving orchestration into adapters.</summary>
internal sealed class DocumentHandler : IDocumentHandler
{
    private const int StoragePageSize = PageRequest.MaximumPageSize;
    private const int PineconeDeleteBatchSize = 1000;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".m2ts", ".webm", ".wmv"
    };

    private readonly DocumentHandlerOptions _options;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IVideoProcessor _videoProcessor;
    private readonly IChunkRepository _chunks;
    private readonly IObjectStorage _storage;
    private readonly IPineconeService _pinecone;

    public DocumentHandler(
        DocumentHandlerOptions options,
        IDocumentProcessor documentProcessor,
        IVideoProcessor videoProcessor,
        IChunkRepository chunks,
        IObjectStorage storage,
        IPineconeService pinecone)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _videoProcessor = videoProcessor ?? throw new ArgumentNullException(nameof(videoProcessor));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _pinecone = pinecone ?? throw new ArgumentNullException(nameof(pinecone));
    }

    public async Task<DocumentHandlerResult> IngestAsync(
        DocumentHandlerInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Content);
        if (string.IsNullOrWhiteSpace(input.FileName))
            throw new ArgumentException("A file name is required.", nameof(input));
        if (!input.Content.CanRead)
            throw new ArgumentException("The upload stream must be readable.", nameof(input));
        if (!string.IsNullOrWhiteSpace(input.ContentType) &&
            !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(input.ContentType, out _))
            throw new ArgumentException("The content type is invalid.", nameof(input));

        cancellationToken.ThrowIfCancellationRequested();
        var fileName = Path.GetFileName(input.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A file name is required.", nameof(input));

        var temporaryPath = await StageUploadAsync(input.Content, cancellationToken).ConfigureAwait(false);
        try
        {
            var processed = await ProcessAsync(fileName, temporaryPath, cancellationToken).ConfigureAwait(false);
            ValidateDocumentId(processed.DocumentId);
            if (processed.Chunks.Count == 0)
                throw new InvalidOperationException("A processor returned no searchable chunks.");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var sourceObject = CreateSourceObject(processed.DocumentId, extension, processed.SourceType);
            var videoUrl = string.Equals(processed.SourceType, "video", StringComparison.OrdinalIgnoreCase)
                ? _storage.GetPublicUrl(sourceObject).AbsoluteUri
                : null;
            var persistedChunks = processed.Chunks.Select(chunk => new Chunk(
                chunk.Id,
                processed.DocumentId,
                chunk.Text,
                chunk.SectionId,
                chunk.SectionTitle,
                [],
                processed.SourceName,
                processed.SourceType,
                VideoUrl: videoUrl)).ToArray();
            var vectorRecords = processed.Chunks.Select(chunk => new PineconeRecord(
                chunk.Id,
                chunk.Text,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["doc_id"] = JsonSerializer.SerializeToElement(processed.DocumentId),
                    ["section_id"] = JsonSerializer.SerializeToElement(chunk.SectionId ?? string.Empty),
                    ["source"] = JsonSerializer.SerializeToElement(processed.SourceName),
                    ["source_type"] = JsonSerializer.SerializeToElement(processed.SourceType),
                })).ToArray();

            var chunkWriteAttempted = false;
            var vectorWriteAttempted = false;
            try
            {
                chunkWriteAttempted = true;
                await _chunks.UpsertAsync(persistedChunks, cancellationToken).ConfigureAwait(false);

                vectorWriteAttempted = true;
                var vectorCount = await _pinecone.UpsertAsync(vectorRecords, cancellationToken).ConfigureAwait(false);
                if (vectorCount != vectorRecords.Length)
                    throw new InvalidOperationException(
                        $"Pinecone reported {vectorCount} upserts for {vectorRecords.Length} chunk records.");

                await using var source = new FileStream(
                    temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await _storage.UploadAsync(
                    sourceObject,
                    source,
                    new UploadOptions(input.ContentType ?? GetContentType(extension), Overwrite: true),
                    cancellationToken).ConfigureAwait(false);

                return new DocumentHandlerResult(
                    processed.DocumentId,
                    processed.SourceName,
                    processed.SourceType,
                    persistedChunks.Length,
                    vectorCount,
                    sourceObject);
            }
            catch (Exception ingestionError)
            {
                var rollbackErrors = new List<Exception>();
                var ids = persistedChunks.Select(chunk => chunk.Id).Distinct(StringComparer.Ordinal).ToArray();

                if (vectorWriteAttempted)
                {
                    try
                    {
                        foreach (var batch in ids.Chunk(PineconeDeleteBatchSize))
                            await _pinecone.DeleteAsync(batch, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors.Add(rollbackError);
                    }
                }

                if (chunkWriteAttempted)
                {
                    try
                    {
                        await _chunks.DeleteByIdsAsync(ids, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors.Add(rollbackError);
                    }
                }

                if (rollbackErrors.Count > 0)
                    throw new AggregateException("Ingestion failed and one or more compensating deletions also failed.",
                        [ingestionError, .. rollbackErrors]);

                throw;
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public async Task<DocumentDeletionResult> DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ValidateDocumentId(documentId);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = await _chunks.GetDocumentMetadataAsync(documentId, cancellationToken).ConfigureAwait(false);
        var chunkIds = new List<string>();
        var offset = 0;
        while (true)
        {
            var page = await _chunks.ListIdsForDocumentAsync(
                documentId, new PageRequest(offset, StoragePageSize), cancellationToken).ConfigureAwait(false);
            chunkIds.AddRange(page.ChunkIds);
            if (!page.HasMore) break;
            offset = checked(offset + page.ChunkIds.Count);
            if (page.ChunkIds.Count == 0) break;
        }

        var uniqueIds = chunkIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var batch in uniqueIds.Chunk(PineconeDeleteBatchSize))
            await _pinecone.DeleteAsync(batch, cancellationToken).ConfigureAwait(false);

        await _chunks.DeleteByIdsAsync(uniqueIds, cancellationToken).ConfigureAwait(false);

        StorageArea[] areas = metadata is null
            ? [StorageArea.Documents, StorageArea.Videos]
            : [string.Equals(metadata.SourceType, "video", StringComparison.OrdinalIgnoreCase)
                ? StorageArea.Videos
                : StorageArea.Documents];
        var removedObjects = 0;
        foreach (var area in areas)
        {
            removedObjects += await DeleteStorageTreeAsync(
                area,
                $"{(area == StorageArea.Videos ? "videos" : "docs")}/{documentId}",
                cancellationToken).ConfigureAwait(false);
        }

        return new DocumentDeletionResult(
            documentId,
            metadata is not null || uniqueIds.Length > 0 || removedObjects > 0,
            uniqueIds.Length,
            removedObjects);
    }

    private async Task<ProcessedDocument> ProcessAsync(
        string fileName,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        if (VideoExtensions.Contains(Path.GetExtension(fileName)))
        {
            await using var videoStream = new FileStream(
                temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var result = await _videoProcessor.ProcessAsync(
                new VideoInput(fileName, videoStream), cancellationToken).ConfigureAwait(false);
            return new ProcessedDocument(
                result.DocumentId,
                result.SourceName,
                result.SourceType,
                result.Chunks.Select(chunk => new ProcessedChunk(
                    chunk.Id, chunk.SectionId, chunk.SectionTitle, chunk.Text)).ToArray());
        }

        await using var documentStream = new FileStream(
            temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await _documentProcessor.ProcessAsync(
            new DocumentInput(fileName, documentStream), cancellationToken).ConfigureAwait(false);
        return new ProcessedDocument(
            document.DocumentId,
            document.SourceName,
            document.SourceType,
            document.Chunks.Select(chunk => new ProcessedChunk(
                chunk.Id, chunk.SectionId, chunk.SectionTitle, chunk.Text)).ToArray());
    }

    private async Task<string> StageUploadAsync(Stream content, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cfc-upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var output = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                totalBytes += read;
                if (totalBytes > _options.MaxUploadBytes)
                    throw new InvalidDataException($"Upload exceeds the configured {_options.MaxUploadBytes} byte size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            if (totalBytes == 0)
                throw new InvalidDataException("The uploaded file is empty.");
            return path;
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
    }

    private static ObjectKey CreateSourceObject(string documentId, string extension, string sourceType)
    {
        var area = string.Equals(sourceType, "video", StringComparison.OrdinalIgnoreCase)
            ? StorageArea.Videos
            : StorageArea.Documents;
        var root = area == StorageArea.Videos ? "videos" : "docs";
        return new ObjectKey(area, $"{root}/{documentId}/original/{documentId}{extension}");
    }

    private async Task<int> DeleteStorageTreeAsync(
        StorageArea area,
        string root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var removedCount = 0;

        while (pending.Count > 0)
        {
            var prefix = pending.Pop();
            var offset = 0;
            var entries = new List<StorageEntry>();
            while (true)
            {
                var page = await _storage.ListAsync(
                    new ObjectPrefix(area, prefix),
                    new PageRequest(offset, StoragePageSize),
                    cancellationToken).ConfigureAwait(false);
                entries.AddRange(page.Items);
                if (!page.HasMore || page.Items.Count == 0) break;
                offset = checked(offset + page.Items.Count);
            }

            foreach (var folder in entries.Where(entry => entry.IsFolder))
                pending.Push(folder.Key.Path);
            var objectKeys = entries.Where(entry => !entry.IsFolder).Select(entry => entry.Key).ToArray();
            if (objectKeys.Length > 0)
            {
                await _storage.DeleteAsync(objectKeys, cancellationToken).ConfigureAwait(false);
                removedCount += objectKeys.Length;
            }
        }

        return removedCount;
    }

    private static string GetContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".eml" => "message/rfc822",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" or ".md" => "text/plain",
        ".3gp" => "video/3gpp",
        ".avi" => "video/x-msvideo",
        ".m4v" => "video/x-m4v",
        ".mkv" => "video/x-matroska",
        ".mov" => "video/quicktime",
        ".mp4" => "video/mp4",
        ".mpeg" or ".mpg" => "video/mpeg",
        ".mts" or ".m2ts" => "video/mp2t",
        ".webm" => "video/webm",
        ".wmv" => "video/x-ms-wmv",
        _ => "application/octet-stream",
    };

    private static void ValidateDocumentId(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (documentId is "." or ".." || documentId.Contains('/') || documentId.Contains('\\') ||
            documentId.Any(char.IsControl))
            throw new ArgumentException("Document ID must be one non-empty storage path segment.", nameof(documentId));
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ProcessedDocument(
        string DocumentId,
        string SourceName,
        string SourceType,
        IReadOnlyList<ProcessedChunk> Chunks);

    private sealed record ProcessedChunk(string Id, string? SectionId, string? SectionTitle, string Text);
}

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
    private readonly IServiceProvider _services;
    private readonly IChunkRepository _chunks;
    private readonly IObjectStorage _storage;
    private readonly IPineconeService _pinecone;

    public DocumentHandler(
        DocumentHandlerOptions options,
        IDocumentProcessor documentProcessor,
        IServiceProvider services,
        IChunkRepository chunks,
        IObjectStorage storage,
        IPineconeService pinecone)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
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
            var imageRoot = DocumentStoragePaths.GetRoot(processed.DocumentId) + "/images";
            var ingestId = Guid.NewGuid().ToString("N");
            var images = processed.Images.Select((image, index) => (Image: image,
                Key: new ObjectKey(StorageArea.Documents, $"{imageRoot}/{ingestId}__img{index + 1:D3}{image.Extension}"))).ToArray();
            var imageKeys = images.ToDictionary(item => item.Image.Id, item => item.Key.Path, StringComparer.Ordinal);
            var persistedChunks = processed.Chunks.Select(chunk => new Chunk(
                chunk.Id,
                processed.DocumentId,
                chunk.Text,
                chunk.SectionId,
                chunk.SectionTitle,
                chunk.ImageIds.Select(id => imageKeys[id]).ToArray(),
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

            var attemptedImageKeys = new List<ObjectKey>();
            var chunkWriteAttempted = false;
            var vectorWriteAttempted = false;
            try
            {
                foreach (var (image, key) in images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var content = new MemoryStream(image.Content.ToArray(), writable: false);
                    attemptedImageKeys.Add(key);
                    try
                    {
                        await _storage.UploadAsync(key, content, new UploadOptions(image.ContentType, Overwrite: false),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (StorageObjectConflictException)
                    {
                        // The provider confirmed that this create did not acquire the key.
                        attemptedImageKeys.RemoveAt(attemptedImageKeys.Count - 1);
                        throw;
                    }
                }

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

                if (attemptedImageKeys.Count > 0)
                {
                    try
                    {
                        await _storage.DeleteAsync(attemptedImageKeys, CancellationToken.None).ConfigureAwait(false);
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
        // Without metadata we cannot determine the source type or discover previously deleted vector IDs.
        // Do not perform a blind cleanup; operators must reconcile any orphaned data separately.
        if (metadata is null) return new DocumentDeletionResult(documentId, false, 0, 0);
        if (!string.Equals(metadata.SourceType, "document", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedDeletionException();
        var chunkIds = new List<string>();
        var offset = 0;
        while (true)
        {
            var page = await _chunks.ListIdsForDocumentAsync(
                documentId, new PageRequest(offset, StoragePageSize), cancellationToken).ConfigureAwait(false);
            chunkIds.AddRange(page.ChunkIds);
            if (!page.HasMore) break;
            if (page.ChunkIds.Count == 0)
                throw new InvalidDataException("Chunk listing did not advance.");
            offset = checked(offset + page.ChunkIds.Count);
        }

        var uniqueIds = chunkIds.Distinct(StringComparer.Ordinal).ToArray();
        if (uniqueIds.Length == 0)
            throw new InvalidDataException("Document metadata exists but no chunk IDs were found.");
        foreach (var batch in uniqueIds.Chunk(PineconeDeleteBatchSize))
            await _pinecone.DeleteAsync(batch, cancellationToken).ConfigureAwait(false);

        // Keep chunk IDs until vector and storage requests complete, so most failures can be retried.
        // A lost response to the final chunk delete still requires manual reconciliation.
        var removedObjects = 0;
        foreach (var root in DocumentStoragePaths.GetLookupRoots(documentId))
            removedObjects += await DeleteStorageTreeAsync(StorageArea.Documents, root, cancellationToken)
                .ConfigureAwait(false);
        await _chunks.DeleteByIdsAsync(uniqueIds, cancellationToken).ConfigureAwait(false);

        return new DocumentDeletionResult(documentId, true, uniqueIds.Length, removedObjects);
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
            var videoProcessor = _services.GetService(typeof(IVideoProcessor)) as IVideoProcessor
                ?? throw new InvalidOperationException("Video processing not configured.");
            var result = await videoProcessor.ProcessAsync(
                new VideoInput(fileName, videoStream), cancellationToken).ConfigureAwait(false);
            return new ProcessedDocument(
                result.DocumentId,
                result.SourceName,
                result.SourceType,
                result.Chunks.Select(chunk => new ProcessedChunk(
                    chunk.Id, chunk.SectionId, chunk.SectionTitle, chunk.Text, [])).ToArray(), []);
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
                chunk.Id, chunk.SectionId, chunk.SectionTitle, chunk.Text, chunk.ImageIds)).ToArray(), document.Images);
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
        if (string.Equals(sourceType, "video", StringComparison.OrdinalIgnoreCase))
            return new ObjectKey(StorageArea.Videos, $"videos/{documentId}/original/{documentId}{extension}");

        var root = DocumentStoragePaths.GetRoot(documentId);
        var name = DocumentStoragePaths.UsesEncodedKey(documentId) ? "document" : documentId;
        return new ObjectKey(StorageArea.Documents, $"{root}/original/{name}{extension}");
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
                foreach (var entry in page.Items)
                {
                    var parent = prefix + "/";
                    if (entry.Key.Area != area || !entry.Key.Path.StartsWith(parent, StringComparison.Ordinal) ||
                        entry.Key.Path.Length == parent.Length || entry.Key.Path[parent.Length..].Contains('/') ||
                        entry.Key.Path[parent.Length..] is "." or "..")
                        throw new InvalidDataException("Storage listing returned an invalid child.");
                    entries.Add(entry);
                }
                if (!page.HasMore) break;
                if (page.Items.Count == 0) throw new InvalidDataException("Storage listing did not advance.");
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
        if (!DocumentId.IsValid(documentId))
            throw new ArgumentException("Invalid document ID.", nameof(documentId));
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
        IReadOnlyList<ProcessedChunk> Chunks,
        IReadOnlyList<DocumentImage> Images);

    private sealed record ProcessedChunk(string Id, string? SectionId, string? SectionTitle, string Text, IReadOnlyList<string> ImageIds);
}

using System.Text.Json.Serialization;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using Microsoft.AspNetCore.Mvc;

namespace CFC.ChatAI.Api.Endpoints;

internal static class StagedDocumentEndpoints
{
    private const int MaxEntries = 10_000;
    private const int MaxDepth = 32;
    private static readonly HashSet<string> StatsExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".txt", ".md" };
    private static readonly EnumerationOptions LocalEntries = new()
        { IgnoreInaccessible = false, AttributesToSkip = 0 };

    public static void MapStagingStats(this WebApplication app, string? root, string? processed)
    {
        app.MapGet("/api/admin/ingestion/stats", (CancellationToken ct) => Scan(root, processed, ct))
            .RequireAuthorization("admin");
    }

    public static void MapStagedIngest(this WebApplication app, string? root, long maxBytes)
    {
        app.MapPost("/api/ingest/document", (StagedRequest request, HttpContext context, IDocumentHandler handler,
                IChunkRepository chunks, DocumentMutations mutations) =>
            IngestAsync(request.Filename, root, maxBytes, context, handler, chunks, mutations))
            .RequireAuthorization("admin").WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    private static IResult Scan(string? root, string? processed, CancellationToken ct)
    {
        try
        {
            var staging = LocalDirectory.Require(root);
            var repository = LocalDirectory.Require(Path.Combine(LocalDirectory.Require(processed), "content_repository"));
            var entries = 0;
            void Count()
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > MaxEntries) throw new ScanLimitException();
            }

            var stems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in new DirectoryInfo(repository).EnumerateFileSystemInfos("*", LocalEntries))
            {
                Count();
                if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                    stems.Add(entry.Name);
            }

            var documents = new List<LocalDocument>();
            void Visit(string directory, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > MaxDepth) throw new ScanLimitException();
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", LocalEntries))
                {
                    Count();
                    var attrs = File.GetAttributes(entry.FullName);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attrs & FileAttributes.Directory) != 0)
                        Visit(entry.FullName, depth + 1);
                    else if (entry is FileInfo file && StatsExtensions.Contains(file.Extension))
                        documents.Add(new(file.Name, file.Length, stems.Contains(Path.GetFileNameWithoutExtension(file.Name)) ? "ingested" : "pending"));
                }
            }
            Visit(staging, 0);
            return Results.Ok(new { total_documents = documents.Count, processing = 0,
                completed = documents.Count(doc => doc.Status == "ingested"),
                documents = documents.OrderBy(doc => doc.Name, StringComparer.Ordinal).ThenBy(doc => doc.Size) });
        }
        catch (ScanLimitException) { return Error(503, "Local staging scan exceeds its limits"); }
        catch (Exception ex) when (LocalDirectory.IsUnavailable(ex)) { return Error(503, "Local staging statistics are unavailable"); }
    }

    private static async Task<IResult> IngestAsync(string? fileName, string? configuredRoot, long maxBytes, HttpContext context,
        IDocumentHandler handler, IChunkRepository chunks, DocumentMutations mutations)
    {
        if (!DocumentUploadEndpoints.IsSafeFilename(fileName)) return Error(400, "Invalid filename");
        if (!DocumentUploadEndpoints.IsAllowedExtension(fileName!)) return Error(415, "Unsupported document type");
        var id = DocumentProcessor.DocumentProcessor.CreateDocumentId(fileName!);
        if (!DocumentId.IsValid(id)) return Error(400, "Invalid document ID derived from filename");
        string root;
        try { root = LocalDirectory.Require(configuredRoot); }
        catch (Exception ex) when (LocalDirectory.IsUnavailable(ex)) { return Error(503, "Local staging is unavailable"); }

        using var lease = mutations.Slots.TryEnter(id);
        if (lease is null) return Error(409, "Another document operation is in progress; retry later");
        var ct = context.RequestAborted;
        var path = Path.Combine(root, fileName!);
        FileStream stream;
        try
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                return Error(404, "Staged file not found");
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException) { return Error(404, "Staged file not found"); }
        catch (Exception ex) when (LocalDirectory.IsUnavailable(ex)) { return Error(503, "Local staging is unavailable"); }
        await using (stream)
        {
            try
            {
                if (stream.Length == 0) return Error(400, "File is empty");
                if (stream.Length > maxBytes) return Error(413, "Upload exceeds the size limit");
                if (await chunks.GetDocumentMetadataAsync(id, ct) is not null)
                    return Error(409, "A document with this ID already exists");
                var result = await handler.IngestAsync(new DocumentHandlerInput(fileName!, stream), ct);
                return Results.Ok(IngestResponse.From(result));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnsupportedDocumentTypeException) { return Error(415, "Unsupported document type"); }
            catch (DocumentProcessingException) { return Error(422, "Document could not be read or contains no extractable text"); }
            catch (DependencyFailureException) { return Error(503, "A dependency is unavailable"); }
            catch (Exception)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("StagedIngest")
                    .LogError("Staged ingestion failed; reconciliation may be required (trace {TraceId})", context.TraceIdentifier);
                return Error(500, "Ingestion failed; contact an operator if reconciliation is needed");
            }
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StagedRequest(string? Filename);
    private sealed record LocalDocument(string Name, long Size, string Status);
    private sealed class ScanLimitException : Exception;
    private static IResult Error(int status, string detail) => Results.Json(new { detail }, statusCode: status);
}

internal sealed record IngestResponse(bool Success, string Message, int ChunksProcessed, int SectionsProcessed,
    int ImagesProcessed, string DocId, string Source, int VectorCount)
{
    public static IngestResponse From(DocumentHandlerResult result) => new(true, "Document ingested.", result.ChunkCount,
        result.SectionCount, result.ImageCount, result.DocumentId, result.SourceName, result.VectorCount);
}

using System.Net;
using System.Text.RegularExpressions;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using Microsoft.AspNetCore.Mvc;

namespace CFC.ChatAI.Api.Endpoints;

public static class DocumentUploadEndpoints
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".docx", ".doc", ".txt", ".md", ".eml" };

    internal static bool IsAllowedExtension(string name) => Extensions.Contains(Path.GetExtension(name));

    public static void MapDocumentUploads(this WebApplication app, long maxBytes, bool enableDev)
    {
        var requestLimit = maxBytes + 1024 * 1024;
        app.MapPost("/api/files/upload", (HttpContext context, IDocumentHandler handler, IChunkRepository chunks) =>
                UploadAsync(context, handler, chunks, maxBytes, requestLimit, checkDuplicate: true))
            .RequireAuthorization("admin")
            .WithMetadata(new RequestSizeLimitAttribute(requestLimit));
        if (enableDev)
            app.MapPost("/api/ingest/dev", (HttpContext context, IDocumentHandler handler, IChunkRepository chunks) =>
                    UploadAsync(context, handler, chunks, maxBytes, requestLimit, checkDuplicate: false))
                .WithMetadata(new RequestSizeLimitAttribute(requestLimit));
    }

    internal static void MapDocumentReplacement(this WebApplication app, long maxBytes)
    {
        var requestLimit = maxBytes + 1024 * 1024;
        app.MapPut("/api/admin/documents/{docId}/replace", (string docId, HttpContext context, IDocumentHandler handler, IChunkRepository chunks) =>
                DocumentId.IsValid(docId)
                    ? UploadAsync(context, handler, chunks, maxBytes, requestLimit, checkDuplicate: true, replaceId: docId)
                    : Task.FromResult(Error(400, "Invalid document ID")))
            .RequireAuthorization("admin")
            .WithMetadata(new RequestSizeLimitAttribute(requestLimit));
    }

    private static async Task<IResult> UploadAsync(HttpContext context, IDocumentHandler handler,
        IChunkRepository chunks, long maxBytes, long requestLimit, bool checkDuplicate, string? replaceId = null)
    {
        if (!checkDuplicate && (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)))
            return Error(403, "Loopback connection required");
        if (context.Request.ContentLength > requestLimit)
            return Error(413, "Upload exceeds the size limit");
        if (!context.Request.HasFormContentType ||
            !context.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Error(400, "Expected multipart form data");

        try
        {
            IFormCollection form;
            try { form = await context.Request.ReadFormAsync(context.RequestAborted); }
            catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { return Error(413, "Upload exceeds the size limit"); }
            catch (InvalidDataException ex) when (ex.Message.StartsWith("Multipart body length limit", StringComparison.Ordinal))
            { return Error(413, "Upload exceeds the size limit"); }
            catch (InvalidDataException) { return Error(400, "Invalid multipart request"); }
            catch (BadHttpRequestException) { return Error(400, "Invalid multipart request"); }
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count != 0)
                return Error(400, "Exactly one file is required");
            var file = form.Files[0];
            var name = file.FileName;
            if (!IsSafeFilename(name))
                return Error(400, "Invalid filename");
            if (!IsAllowedExtension(name))
                return Error(415, "Unsupported document type");
            if (file.Length == 0) return Error(400, "File is empty");
            if (file.Length > maxBytes) return Error(413, "Upload exceeds the size limit");
            var documentId = DocumentProcessor.DocumentProcessor.CreateDocumentId(name);
            if (!DocumentId.IsValid(documentId))
                return Error(400, "Invalid document ID derived from filename");
            if (replaceId is not null && !string.Equals(documentId, replaceId, StringComparison.Ordinal))
                return Error(409, "Replacement filename must derive the existing document ID");
            using var lease = context.RequestServices.GetRequiredService<DocumentMutations>().Slots.TryEnter(documentId);
            if (lease is null) return Error(409, "Another document operation is in progress; retry later");
            if (replaceId is null && checkDuplicate && await chunks.GetDocumentMetadataAsync(documentId, context.RequestAborted) is not null)
                return Error(409, "A document with this ID already exists");

            await using var stream = file.OpenReadStream();
            // Client MIME is not trusted; the handler selects a content type from the extension.
            var input = new DocumentHandlerInput(name, stream);
            if (replaceId is null)
            {
                var result = await handler.IngestAsync(input, context.RequestAborted);
                return Results.Ok(new UploadResult(result.DocumentId, result.SourceName, result.SourceType,
                    result.ChunkCount, result.VectorCount));
            }
            var replacement = await handler.ReplaceAsync(replaceId, input, context.RequestAborted);
            return replacement is null ? Error(404, "Document metadata not found") :
                Results.Ok(new { success = true, message = "Document replaced.", ingestion = IngestResponse.From(replacement) });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (ReplacementConflictException) { return Error(409, "Replacement must preserve the document ID and use the existing document format; video is unsupported"); }
        catch (ReplacementPartialFailureException ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocumentReplacement")
                .LogError("Replacement incomplete at {Stage} (trace {TraceId})", ex.Stage, context.TraceIdentifier);
            if (context.RequestAborted.IsCancellationRequested) throw new OperationCanceledException(context.RequestAborted);
            return Results.Json(new { detail = "Replacement may be incomplete; reconcile before retrying.", stage = ex.Stage,
                reference = context.TraceIdentifier }, statusCode: ex.InnerException is DependencyFailureException ? 503 : 500);
        }
        catch (UnsupportedDocumentTypeException) { return Error(415, "Unsupported document type"); }
        catch (DocumentProcessingException) { return Error(422, "Document could not be read or contains no extractable text"); }
        catch (DependencyFailureException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocumentUpload")
                .LogWarning("Upload dependency unavailable (trace {TraceId})", context.TraceIdentifier);
            return Error(503, "A dependency is unavailable");
        }
        catch (Exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocumentUpload")
                .LogError("Upload failed; reconciliation may be required (trace {TraceId})", context.TraceIdentifier);
            return Error(500, "Upload failed; contact an operator if reconciliation is needed");
        }
    }

    internal static bool IsSafeFilename(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && name is not ("." or "..") &&
        !name.EndsWith(' ') && !name.EndsWith('.') && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0 &&
        !name.Any(char.IsControl) && !Regex.IsMatch(name, "%[0-9A-Fa-f]{2}") && !IsReservedWindowsName(name);

    private static bool IsReservedWindowsName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] is (>= '1' and <= '9') or '¹' or '²' or '³');
    }

    private static IResult Error(int status, string detail) => Results.Json(new { detail }, statusCode: status);
    private sealed record UploadResult(string DocumentId, string SourceName, string SourceType, int ChunkCount, int VectorCount);
}

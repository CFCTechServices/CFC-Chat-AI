using System.Net;
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

    private static async Task<IResult> UploadAsync(HttpContext context, IDocumentHandler handler,
        IChunkRepository chunks, long maxBytes, long requestLimit, bool checkDuplicate)
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
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." ||
                name.EndsWith(' ') || name.EndsWith('.') || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ||
                name.Any(char.IsControl) || IsReservedWindowsName(name))
                return Error(400, "Invalid filename");
            if (!Extensions.Contains(Path.GetExtension(name)))
                return Error(415, "Unsupported document type");
            if (file.Length == 0) return Error(400, "File is empty");
            if (file.Length > maxBytes) return Error(413, "Upload exceeds the size limit");
            var documentId = DocumentProcessor.DocumentProcessor.CreateDocumentId(name);
            if (!DocumentId.IsValid(documentId))
                return Error(400, "Invalid document ID derived from filename");
            if (checkDuplicate && await chunks.GetDocumentMetadataAsync(documentId, context.RequestAborted) is not null)
                return Error(409, "A document with this ID already exists");

            await using var stream = file.OpenReadStream();
            // Client MIME is not trusted; the handler selects a content type from the extension.
            var result = await handler.IngestAsync(new DocumentHandlerInput(name, stream), context.RequestAborted);
            return Results.Ok(new UploadResult(result.DocumentId, result.SourceName, result.SourceType,
                result.ChunkCount, result.VectorCount));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
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

    private static bool IsReservedWindowsName(string name)
    {
        var stem = name.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    private static IResult Error(int status, string detail) => Results.Json(new { detail }, statusCode: status);
    private sealed record UploadResult(string DocumentId, string SourceName, string SourceType, int ChunkCount, int VectorCount);
}

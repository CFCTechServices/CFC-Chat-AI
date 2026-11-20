using System.Net;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Api.Endpoints;

public static class DocumentDeletionEndpoints
{
    public static void MapDocumentDeletions(this WebApplication app, bool enableDev)
    {
        app.MapDelete("/api/admin/documents/{docId}",
                (string docId, HttpContext context, IDocumentHandler handler) => DeleteAsync(docId, context, handler))
            .RequireAuthorization("admin");
        if (enableDev)
            app.MapDelete("/api/ingest/dev/{docId}", (string docId, HttpContext context, IDocumentHandler handler) =>
                context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
                    ? DeleteAsync(docId, context, handler)
                    : Task.FromResult<IResult>(Error(403, "Loopback connection required")));
    }

    private static async Task<IResult> DeleteAsync(string docId, HttpContext context, IDocumentHandler handler)
    {
        if (!DocumentId.IsValid(docId)) return Error(400, "Invalid document ID");
        try
        {
            var result = await handler.DeleteAsync(docId, context.RequestAborted);
            return result.Found
                ? Results.Ok(new { success = true, message = "Document deleted." })
                : Error(404, "Document metadata not found; reconcile manually if a previous deletion was incomplete");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (UnsupportedDeletionException) { return Error(409, "Deletion is supported only for documents with known document metadata"); }
        catch (DependencyFailureException)
        {
            LogFailure(context, "dependency unavailable");
            return Error(503, $"Deletion failed or is incomplete; reconcile manually. Reference: {context.TraceIdentifier}");
        }
        catch (Exception)
        {
            LogFailure(context, "deletion failed");
            return Error(500, $"Deletion failed or is incomplete; reconcile manually. Reference: {context.TraceIdentifier}");
        }
    }

    private static void LogFailure(HttpContext context, string reason) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocumentDeletion")
            .LogError("Document {Reason}; manual reconciliation may be required (trace {TraceId})", reason, context.TraceIdentifier);

    private static IResult Error(int status, string detail) => Results.Json(new { detail }, statusCode: status);
}

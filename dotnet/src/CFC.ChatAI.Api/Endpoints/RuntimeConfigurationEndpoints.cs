using CFC.ChatAI.Api.Services;

namespace CFC.ChatAI.Api.Endpoints;

public static class RuntimeConfigurationEndpoints
{
    public static void MapRuntimeConfiguration(this WebApplication app)
    {
        app.MapGet("/api/auth/config", (RuntimeDiagnostics diagnostics) => diagnostics.GetAuthConfig())
            .WithTags("auth");
        app.MapGet("/api/visibility/vector-store", async (RuntimeDiagnostics diagnostics, CancellationToken ct) =>
        {
            try { return Results.Ok(await diagnostics.GetVectorStatsAsync(ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return Results.Json(new { detail = "Failed to fetch vector store stats" }, statusCode: 500); }
        }).WithTags("visibility");
    }
}

using CFC.ChatAI.Api.Contracts;
using CFC.ChatAI.Api.Services;

namespace CFC.ChatAI.Api.Endpoints;

public static class HealthEndpoints
{
    // Python parity: app/api/endpoints/health.py :: health_check
    // Router in main.py mounts it at prefix="/api", route "/health" → GET /api/health
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/api/health", (ApiInfo info) =>
            new HealthResponse(
                Ok: true,
                Message: "CFC Animal Feed Software Chatbot API is running",
                Version: info.Version))
            .WithTags("health");

        app.MapGet("/api/health/detailed", (RuntimeDiagnostics diagnostics, ApiInfo info, CancellationToken ct) =>
            diagnostics.GetHealthAsync(info.Version, ct))
            .WithTags("health");
    }
}

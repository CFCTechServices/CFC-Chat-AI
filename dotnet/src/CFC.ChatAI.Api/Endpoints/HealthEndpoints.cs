using CFC.ChatAI.Api.Contracts;

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
                Message: "CFC Animal Feed Software Chatbot API is running (C#)",
                Version: info.Version))
            .WithTags("health");
    }
}

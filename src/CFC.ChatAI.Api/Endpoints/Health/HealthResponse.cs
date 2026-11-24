namespace CFC.ChatAI.Api.Features.Health;

public sealed record HealthResponse(bool Ok, string Message, string? Version);

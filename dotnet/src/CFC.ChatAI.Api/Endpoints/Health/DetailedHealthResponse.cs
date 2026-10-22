namespace CFC.ChatAI.Api.Features.Health;

public sealed record DetailedHealthResponse(
    bool Ok,
    string Version,
    string Message,
    IReadOnlyDictionary<string, object?> Services);

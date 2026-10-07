namespace CFC.ChatAI.Api.Contracts;

/// <summary>
/// Exact parity with Python <c>app/api/models/responses.py :: HealthResponse</c>.
/// Serialized with the global <c>JsonNamingPolicy.SnakeCaseLower</c> policy
/// (see Program.cs), so no per-property attributes are needed for this shape.
/// </summary>
public sealed record HealthResponse(bool Ok, string Message, string? Version);

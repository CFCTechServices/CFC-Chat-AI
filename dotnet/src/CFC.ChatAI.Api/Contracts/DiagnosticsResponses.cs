using System.Text.Json.Serialization;

namespace CFC.ChatAI.Api.Contracts;

public sealed record AuthConfigResponse(
    [property: JsonPropertyName("supabaseUrl")] string? SupabaseUrl,
    [property: JsonPropertyName("supabaseKey")] string? SupabaseKey);

// Optional per-service fields must be omitted, unlike nullable fields in Pydantic response models.
public sealed record ServiceStatus(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Index = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Deployment = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model = null);

public sealed record DetailedHealthResponse(bool Ok, string Version, string Message,
    IReadOnlyDictionary<string, ServiceStatus> Services);
public sealed record NamespaceStats(string Name, long VectorCount);
public sealed record VectorStoreStatsResponse(bool Success, string IndexName, long TotalVectors,
    int? Dimension, double? IndexFullness, IReadOnlyList<NamespaceStats> Namespaces, string? Error = null);

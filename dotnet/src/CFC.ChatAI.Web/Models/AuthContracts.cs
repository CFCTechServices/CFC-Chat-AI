using System.Text.Json.Serialization;

namespace CFC.ChatAI.Web.Models;

public sealed record AuthConfigResponse(
    [property: JsonPropertyName("supabaseUrl")] string? SupabaseUrl,
    [property: JsonPropertyName("supabaseKey")] string? SupabaseKey);

public sealed record EmailRequest(string Email);

public sealed record CheckEmailResponse(bool Eligible, string Message);

public sealed record ForgotPasswordResponse(bool Success, string Message);

public sealed record ProfileResponse(
    string Id,
    string Email,
    string Role,
    string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("full_name")] string? FullName = null,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl = null);

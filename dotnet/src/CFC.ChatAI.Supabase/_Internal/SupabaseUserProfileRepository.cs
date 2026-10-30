using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Profiles;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseUserProfileRepository(SupabaseTransport transport) : IUserProfileRepository
{
    private const string Columns = "id,email,full_name,avatar_url,role,status,created_at,deleted_at,deleted_by";

    public async Task<Profile?> GetCurrentAsync(
        string userId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        var uri = transport.RestUri($"profiles?select={Columns}&id=eq.{SupabaseTransport.Escape(userId)}&limit=1");
        using var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToProfile();
    }

    public async Task<Profile?> UpdateCurrentAsync(
        string userId,
        string accessToken,
        UpdateOwnProfile update,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentNullException.ThrowIfNull(update);
        var body = new Dictionary<string, string>();
        if (update.FullName is not null)
            body["full_name"] = update.FullName;
        if (update.AvatarUrl is not null)
            body["avatar_url"] = update.AvatarUrl;
        if (body.Count == 0)
            throw new ArgumentException("At least one self-service profile field must be supplied.", nameof(update));

        var uri = transport.RestUri($"profiles?select={Columns}&id=eq.{SupabaseTransport.Escape(userId)}");
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Patch, uri, body);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToProfile();
    }

    private static void ValidateScope(string userId, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
    }
}

internal sealed class ProfileWire
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("full_name")] public string? FullName { get; init; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("deleted_at")] public DateTimeOffset? DeletedAt { get; init; }
    [JsonPropertyName("deleted_by")] public string? DeletedBy { get; init; }

    internal Profile ToProfile() => new(Id, Email, FullName, AvatarUrl, Role, Status, CreatedAt, DeletedAt, DeletedBy);
}

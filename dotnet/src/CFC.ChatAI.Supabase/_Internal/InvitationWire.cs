using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Invitations;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class InvitationWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("used_by")]
    public string? UsedBy { get; init; }

    [JsonPropertyName("is_registered")]
    public bool? IsRegistered { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    internal Invitation ToInvitation() => new(
        Id, Email, CreatedBy, UsedBy, IsRegistered, CreatedAt, ExpiresAt);
}

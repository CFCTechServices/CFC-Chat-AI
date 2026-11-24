namespace CFC.ChatAI.Supabase.Invitations;

/// <summary>A persisted email invitation. Expiry and registration policy are interpreted by callers.</summary>
public sealed record Invitation(
    string Id,
    string Email,
    string? CreatedBy,
    string? UsedBy,
    bool? IsRegistered,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

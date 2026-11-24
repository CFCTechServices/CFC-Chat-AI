namespace CFC.ChatAI.Supabase.Profiles;

/// <summary>Persisted profile fields returned by profile reads.</summary>
public sealed record Profile(
    string Id,
    string? Email,
    string? FullName,
    string? AvatarUrl,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    string? DeletedBy);

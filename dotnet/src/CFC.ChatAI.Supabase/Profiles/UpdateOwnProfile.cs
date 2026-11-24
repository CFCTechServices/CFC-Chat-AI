namespace CFC.ChatAI.Supabase.Profiles;

/// <summary>Allow-listed self-service fields. Role and account-state fields are intentionally absent.</summary>
public sealed record UpdateOwnProfile(string? FullName = null, string? AvatarUrl = null);

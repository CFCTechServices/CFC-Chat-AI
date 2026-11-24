namespace CFC.ChatAI.Supabase.Identity;

/// <summary>Provider-neutral identity details required by application workflows.</summary>
public sealed record IdentityUser(string Id, string? Email);

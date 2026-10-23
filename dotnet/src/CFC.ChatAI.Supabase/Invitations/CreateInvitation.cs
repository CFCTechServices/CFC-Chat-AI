namespace CFC.ChatAI.Supabase.Invitations;

/// <summary>Input columns for an invitation; ID, creation time, registration state, and expiry are database-owned.</summary>
public sealed record CreateInvitation(string Email, string? CreatedBy);

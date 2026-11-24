namespace CFC.ChatAI.Supabase.Invitations;

/// <summary>Sanitized conflict raised when the database's active-invitation uniqueness rule rejects an insert.</summary>
public sealed class InvitationConflictException : Exception
{
    public InvitationConflictException()
        : base("An active invitation already exists for this email.")
    {
    }
}

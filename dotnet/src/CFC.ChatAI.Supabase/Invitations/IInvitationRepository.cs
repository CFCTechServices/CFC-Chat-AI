namespace CFC.ChatAI.Supabase.Invitations;

/// <summary>Privileged invitation persistence only; eligibility, expiry, and authorization policy belong to callers.</summary>
public interface IInvitationRepository
{
    /// <summary>Returns the latest invitation by creation time, or null when none exists.</summary>
    Task<Invitation?> GetLatestByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Returns the latest unregistered invitation, or null when none exists.</summary>
    Task<Invitation?> GetUnregisteredByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Inserts an invitation and returns database-generated values. Known active-invite uniqueness races throw <see cref="InvitationConflictException"/>.</summary>
    Task<Invitation> InsertAsync(CreateInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>Deletes an invitation by ID; deleting an absent row is a no-op.</summary>
    Task DeleteByIdAsync(string invitationId, CancellationToken cancellationToken = default);

    /// <summary>Deletes all invitation rows with the exact email; deleting no rows is a no-op.</summary>
    Task DeleteByEmailAsync(string email, CancellationToken cancellationToken = default);
}

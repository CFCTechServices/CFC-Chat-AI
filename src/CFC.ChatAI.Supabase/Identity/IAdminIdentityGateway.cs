namespace CFC.ChatAI.Supabase.Identity;

/// <summary>
/// Privileged identity operations. Inject only into authorized administrative workflows;
/// profile roles/status and cross-resource account orchestration belong elsewhere.
/// </summary>
public interface IAdminIdentityGateway
{
    Task<IdentityUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task SetBannedAsync(string userId, bool banned, CancellationToken cancellationToken = default);
    Task RevokeSessionsAsync(string userId, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}

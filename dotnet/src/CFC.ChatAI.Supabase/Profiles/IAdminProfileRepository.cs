using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Profiles;

/// <summary>Privileged profile persistence operations; authorization and policy belong to callers.</summary>
public interface IAdminProfileRepository
{
    /// <summary>Lists profiles in stable created-at descending order, one bounded page at a time.</summary>
    Task<Page<Profile>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Returns a profile by ID, or null when absent.</summary>
    Task<Profile?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Returns a profile by exact email, or null when absent.</summary>
    Task<Profile?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Updates the role without enforcing which roles or targets are permitted.</summary>
    Task<Profile?> UpdateRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>Updates status and deletion metadata without enforcing lifecycle policy.</summary>
    Task<Profile?> UpdateAccountStateAsync(
        string userId,
        string status,
        DateTimeOffset? deletedAt,
        string? deletedBy,
        CancellationToken cancellationToken = default);
}

namespace CFC.ChatAI.Supabase.Profiles;

/// <summary>Profile operations using a caller's access token and Supabase RLS.</summary>
public interface IUserProfileRepository
{
    /// <summary>Returns the caller's profile, or null when no visible row exists.</summary>
    Task<Profile?> GetCurrentAsync(string userId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Updates only full_name/avatar_url. Empty updates are rejected; no visible row returns null.</summary>
    Task<Profile?> UpdateCurrentAsync(
        string userId,
        string accessToken,
        UpdateOwnProfile update,
        CancellationToken cancellationToken = default);
}

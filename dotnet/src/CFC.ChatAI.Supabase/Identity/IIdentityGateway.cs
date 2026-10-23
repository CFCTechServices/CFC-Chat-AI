namespace CFC.ChatAI.Supabase.Identity;

/// <summary>Non-administrative identity operations used by authenticated application flows.</summary>
public interface IIdentityGateway
{
    /// <summary>Validates an access token. Returns null only when the token is invalid or expired.</summary>
    Task<IdentityUser?> GetUserAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Starts the provider's password-recovery flow for an email address.</summary>
    Task RequestPasswordRecoveryAsync(
        string email,
        Uri? redirectTo = null,
        CancellationToken cancellationToken = default);
}

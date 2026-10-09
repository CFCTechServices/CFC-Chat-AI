using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Models;

namespace CFC.ChatAI.Api.Application.Services;

public sealed class RecoveryService(IIdentityGateway identity)
{
    public async Task<RecoveryResponse> RequestAsync(string email, CancellationToken ct) =>
        await identity.RecoverAsync(email, ct) == Outcome.RateLimited
            ? new(false, "Too many reset attempts. Please wait a few minutes before trying again.")
            : new(true, "If an account exists with this email, a password reset link has been sent.");
}
public sealed class EligibilityService(IInvitationRepository invitations, TimeProvider time, ILogger<EligibilityService> logger)
{
    public async Task<EligibilityResponse> CheckAsync(string email, CancellationToken ct)
    {
        var result = await invitations.FindLatestUnregisteredAsync(email, ct);
        if (result.Outcome is not (Outcome.Success or Outcome.NotFound))
            logger.LogWarning("Invitation eligibility lookup failed: {Classification}", result.Outcome);
        return result.Outcome switch
        {
            Outcome.NotFound => new(false, "This email has not been invited. Please contact your administrator."),
            Outcome.Success when result.Value!.ExpiresAt <= time.GetUtcNow() =>
                new(false, "Your invitation has expired. Please contact your administrator for a new one."),
            Outcome.Success => new(true, "Email is eligible for registration."),
            _ => new(false, "Unable to verify email. Please try again later.")
        };
    }
}
public sealed class ProfileService(IProfileRepository profiles)
{
    public Task<Result<Profile>> ReadAsync(VerifiedUser user, CancellationToken ct) => profiles.ReadSelfAsync(user, ct);
    public Task<Result<Profile>> UpdateAsync(VerifiedUser user, ProfilePatch patch, CancellationToken ct) =>
        profiles.UpdateSelfAsync(user, patch, ct);
}

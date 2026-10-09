using CFC.ChatAI.Api.Application.Models;

namespace CFC.ChatAI.Api.Application.Abstractions;

public interface IIdentityGateway
{
    Task<Result<VerifiedIdentity>> ValidateAsync(string token, CancellationToken cancellationToken);
    Task<Outcome> RecoverAsync(string email, CancellationToken cancellationToken);
}
public interface IProfileRepository
{
    Task<Result<Profile>> ReadSelfAsync(VerifiedUser user, CancellationToken cancellationToken);
    Task<Result<Profile>> UpdateSelfAsync(VerifiedUser user, ProfilePatch patch, CancellationToken cancellationToken);
}
public interface IProfileAccessReader
{
    Task<Result<ProfileAccessState>> ReadAsync(string verifiedId, CancellationToken cancellationToken);
}
public interface IInvitationRepository
{
    Task<Result<Invitation>> FindLatestUnregisteredAsync(string email, CancellationToken cancellationToken);
}

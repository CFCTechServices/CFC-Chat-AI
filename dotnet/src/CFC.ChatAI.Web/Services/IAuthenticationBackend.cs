using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public interface IAuthenticationBackend
{
    Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken cancellationToken = default);
    Task<CheckEmailResponse> CheckEmailAsync(EmailRequest request, CancellationToken cancellationToken = default);
    Task<ProfileResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default);
}

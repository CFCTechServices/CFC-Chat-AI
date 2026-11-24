using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services.Api;

public sealed partial class ChatApiClient
{
    public Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AuthConfigResponse>(HttpMethod.Get, "api/auth/config", cancellationToken);

    public Task<CheckEmailResponse> CheckEmailAsync(EmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CheckEmailResponse>(HttpMethod.Post, "api/auth/check-email", cancellationToken, body: request);

    public Task<ForgotPasswordResponse> RequestPasswordResetAsync(EmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ForgotPasswordResponse>(HttpMethod.Post, "api/auth/forgot-password", cancellationToken, body: request);

    public Task<ProfileResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileResponse>(HttpMethod.Get, "api/profile/me", cancellationToken, Bearer(accessToken));

    public Task<ProfileResponse> UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileResponse>(HttpMethod.Patch, "api/profile/me", cancellationToken, Bearer(accessToken), request);
}

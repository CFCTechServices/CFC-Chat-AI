using System.Net;
using CFC.ChatAI.Supabase.Identity;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseIdentityGateway(SupabaseTransport transport) : IIdentityGateway
{
    public async Task<IdentityUser?> GetUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.AuthUri("user"));
        try
        {
            using var response = await transport.SendUserAsync(request, accessToken, cancellationToken)
                .ConfigureAwait(false);
            var user = await SupabaseTransport.ReadJsonAsync<UserWire>(response, cancellationToken)
                .ConfigureAwait(false);
            return new IdentityUser(user.Id, user.Email);
        }
        catch (SupabaseRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }
        catch (SupabaseRequestException)
        {
            throw new CFC.ChatAI.Supabase.Common.DependencyFailureException("Supabase", "HTTP request");
        }
    }

    public async Task RequestPasswordRecoveryAsync(
        string email,
        Uri? redirectTo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var suffix = redirectTo is null
            ? "recover"
            : $"recover?redirect_to={SupabaseTransport.Escape(redirectTo.ToString())}";
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, transport.AuthUri(suffix), new { email });
        try
        {
            using var response = await transport.SendAnonAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SupabaseRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new PasswordRecoveryRateLimitedException();
        }
    }
}

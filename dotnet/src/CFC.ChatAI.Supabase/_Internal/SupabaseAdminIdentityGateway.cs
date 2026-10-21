using System.Net;
using CFC.ChatAI.Supabase.Identity;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseAdminIdentityGateway(SupabaseTransport transport) : IAdminIdentityGateway
{
    public async Task<IdentityUser?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.AuthUri($"admin/users/{SupabaseTransport.Escape(userId)}");
        using var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        try
        {
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            var user = await SupabaseTransport.ReadJsonAsync<UserWire>(response, cancellationToken)
                .ConfigureAwait(false);
            return new IdentityUser(user.Id, user.Email);
        }
        catch (SupabaseRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SetBannedAsync(
        string userId,
        bool banned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.AuthUri($"admin/users/{SupabaseTransport.Escape(userId)}");
        using var request = SupabaseTransport.JsonRequest(
            HttpMethod.Put, uri, new { ban_duration = banned ? "876000h" : "none" });
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.AuthUri($"admin/users/{SupabaseTransport.Escape(userId)}/logout");
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, new { scope = "global" });
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.AuthUri($"admin/users/{SupabaseTransport.Escape(userId)}");
        using var request = SupabaseTransport.Request(HttpMethod.Delete, uri);
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

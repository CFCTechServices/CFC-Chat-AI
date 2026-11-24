using System.Net;
using CFC.ChatAI.Web.Components;

namespace CFC.ChatAI.Web.Services;

public sealed partial class SupabaseSessionService
{
    internal bool HasAdminAccess(string ownerId) => CanAccessChat && State.Profile?.Id == ownerId &&
        AppDestination.Admin.Allows(State.Profile);

    internal Task<T> WithAdminTokenAsync<T>(string ownerId, Func<string, CancellationToken, Task<T>> request,
        CancellationToken ct) => WithAccessTokenAsync(ownerId, async (token, cancellation) =>
        {
            RequireAdmin(ownerId);
            var result = await request(token, cancellation);
            cancellation.ThrowIfCancellationRequested();
            RequireAdmin(ownerId);
            return result;
        }, ct);

    private void RequireAdmin(string ownerId)
    {
        if (!HasAdminAccess(ownerId))
            throw new HttpRequestException("Administrator access is required.", null, HttpStatusCode.Forbidden);
    }
}

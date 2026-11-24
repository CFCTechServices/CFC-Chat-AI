using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseHealthGateway(SupabaseTransport transport) : ISupabaseHealthGateway
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri("profiles?select=id&limit=1"));
        try
        {
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }
}

using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Profiles;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseAdminProfileRepository(SupabaseTransport transport) : IAdminProfileRepository
{
    private const string Columns = "id,email,full_name,avatar_url,role,status,created_at,deleted_at,deleted_by";

    public async Task<Page<Profile>> ListAsync(
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        // Fetch one extra row to provide a continuation indicator without an unbounded count query.
        var uri = transport.RestUri(
            $"profiles?select={Columns}&order=created_at.desc,id.asc&offset={page.Offset}&limit={page.Limit + 1}");
        using var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Length > page.Limit;
        var items = rows.Take(page.Limit).Select(row => row.ToProfile()).ToArray();
        return new Page<Profile>(items, hasMore);
    }

    public async Task<Profile?> GetByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.RestUri($"profiles?select={Columns}&id=eq.{SupabaseTransport.Escape(userId)}&limit=1");
        using var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToProfile();
    }

    public async Task<Profile?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var uri = transport.RestUri($"profiles?select={Columns}&email=eq.{SupabaseTransport.Escape(email)}&limit=1");
        using var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToProfile();
    }

    public Task<Profile?> UpdateRoleAsync(
        string userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return UpdateAsync(userId, new Dictionary<string, string> { ["role"] = role }, cancellationToken);
    }

    public Task<Profile?> UpdateAccountStateAsync(
        string userId,
        string status,
        DateTimeOffset? deletedAt,
        string? deletedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return UpdateAsync(userId, new Dictionary<string, object?>
        {
            ["status"] = status,
            ["deleted_at"] = deletedAt,
            ["deleted_by"] = deletedBy,
        }, cancellationToken);
    }

    private async Task<Profile?> UpdateAsync(
        string userId,
        object body,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var uri = transport.RestUri($"profiles?select={Columns}&id=eq.{SupabaseTransport.Escape(userId)}");
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Patch, uri, body);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<ProfileWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToProfile();
    }
}

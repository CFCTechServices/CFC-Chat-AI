using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Invitations;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseInvitationRepository(SupabaseTransport transport) : IInvitationRepository
{
    private const string Columns = "id,email,created_by,used_by,is_registered,created_at,expires_at";
    private const string ActiveInviteIndex = "idx_one_active_invite_per_email";

    public Task<Invitation?> GetLatestByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return GetOneAsync(
            $"invitations?select={Columns}&email=eq.{SupabaseTransport.Escape(email)}&order=created_at.desc,id.asc&limit=1",
            cancellationToken);
    }

    public Task<Invitation?> GetUnregisteredByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return GetOneAsync(
            $"invitations?select={Columns}&email=eq.{SupabaseTransport.Escape(email)}&is_registered=eq.false&order=created_at.desc,id.asc&limit=1",
            cancellationToken);
    }

    public async Task<Invitation> InsertAsync(
        CreateInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invitation.Email);

        var body = new Dictionary<string, string?>
        {
            ["email"] = invitation.Email,
            ["created_by"] = invitation.CreatedBy,
        };
        var uri = transport.RestUri($"invitations?select={Columns}");
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, body);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        HttpResponseMessage response;
        try
        {
            response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SupabaseRequestException exception) when (
            exception.IsUniqueViolation && exception.ConstraintName == ActiveInviteIndex)
        {
            throw new InvitationConflictException();
        }

        using (response)
        {
            var rows = await SupabaseTransport.ReadJsonAsync<InvitationWire[]>(response, cancellationToken)
                .ConfigureAwait(false);
            if (rows.Length == 0)
                throw new DependencyFailureException("Supabase", "invitation insert response");
            return rows[0].ToInvitation();
        }
    }

    public async Task DeleteByIdAsync(string invitationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        await DeleteAsync($"invitations?id=eq.{SupabaseTransport.Escape(invitationId)}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        await DeleteAsync($"invitations?email=eq.{SupabaseTransport.Escape(email)}", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Invitation?> GetOneAsync(string path, CancellationToken cancellationToken)
    {
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(path));
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<InvitationWire[]>(response, cancellationToken).ConfigureAwait(false);
        return rows.Length == 0 ? null : rows[0].ToInvitation();
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var request = SupabaseTransport.Request(HttpMethod.Delete, transport.RestUri(path));
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Models;
using CFC.ChatAI.Api.Configuration;

namespace CFC.ChatAI.Api.Infrastructure.Supabase;

internal sealed class SupabaseIdentityGateway(SupabaseTransport transport, SupabaseOptions options) : IIdentityGateway
{
    public async Task<Result<VerifiedIdentity>> ValidateAsync(string token, CancellationToken ct)
    {
        var response = await transport.SendAsync(HttpMethod.Get, "/auth/v1/user", true, token, null, false, true, ct);
        if (response.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new(Outcome.InvalidToken);
        if (response.Outcome != Outcome.Success) return new(response.Outcome);
        if (response.Body is not { ValueKind: JsonValueKind.Object } row) return new(Outcome.Malformed);
        if (!row.TryGetProperty("id", out var id) || id.ValueKind == JsonValueKind.Null) return new(Outcome.InvalidToken);
        if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) return new(Outcome.Malformed);
        return Result<VerifiedIdentity>.Ok(new(id.GetString()!));
    }
    public async Task<Outcome> RecoverAsync(string email, CancellationToken ct)
    {
        var response = await transport.SendAsync(HttpMethod.Post,
            "/auth/v1/recover?redirect_to=" + SupabaseTransport.Query(options.FrontendBaseUrl.TrimEnd('/')),
            true, null, new { email }, false, false, ct);
        return response.Status == HttpStatusCode.TooManyRequests ? Outcome.RateLimited : response.Outcome;
    }
}

internal static class ProviderMapping
{
    internal const string Projection = "id,email,full_name,avatar_url,role,status,created_at";
    internal static Result<JsonElement> First(ProviderResponse response)
    {
        if (response.Outcome != Outcome.Success) return new(response.Outcome);
        if (response.Body is not { ValueKind: JsonValueKind.Array } rows) return new(Outcome.Malformed);
        if (rows.GetArrayLength() == 0) return new(Outcome.NotFound);
        return rows[0].ValueKind == JsonValueKind.Object ? Result<JsonElement>.Ok(rows[0]) : new(Outcome.Malformed);
    }
    internal static string Required(JsonElement row, string key) =>
        row.GetProperty(key).GetString() ?? throw new JsonException();
    internal static string? Optional(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    internal static bool OffsetTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return Regex.IsMatch(value, @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }
    internal static Result<Profile> Profile(ProviderResponse response, string expectedId)
    {
        var first = First(response);
        if (first.Outcome != Outcome.Success) return new(first.Outcome);
        try
        {
            var row = first.Value;
            var id = Required(row, "id");
            var created = Required(row, "created_at");
            if (id != expectedId || !OffsetTimestamp(created, out _)) return new(Outcome.Malformed);
            return Result<Profile>.Ok(new(id, Required(row, "email"), Optional(row, "full_name"),
                Optional(row, "avatar_url"), Required(row, "role"), Required(row, "status"), created));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(Outcome.Malformed); }
    }
}

internal sealed class SupabaseProfileRepository(SupabaseTransport transport) : IProfileRepository
{
    public async Task<Result<Profile>> ReadSelfAsync(VerifiedUser user, CancellationToken ct) =>
        ProviderMapping.Profile(await transport.SendAsync(HttpMethod.Get, Path(user.Id) + "&limit=1",
            false, user.AccessToken, null, false, true, ct), user.Id);

    public async Task<Result<Profile>> UpdateSelfAsync(VerifiedUser user, ProfilePatch patch, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>();
        if (patch.FullName is not null) fields["full_name"] = patch.FullName;
        if (patch.AvatarUrl is not null) fields["avatar_url"] = patch.AvatarUrl;
        return ProviderMapping.Profile(await transport.SendAsync(HttpMethod.Patch, Path(user.Id),
            false, user.AccessToken, fields, true, true, ct), user.Id);
    }
    private static string Path(string id) => "/rest/v1/profiles?select=" + ProviderMapping.Projection + "&id=" + SupabaseTransport.Equal(id);
}

internal sealed class SupabaseProfileAccessReader(SupabaseTransport transport) : IProfileAccessReader
{
    public async Task<Result<ProfileAccessState>> ReadAsync(string verifiedId, CancellationToken ct)
    {
        var first = ProviderMapping.First(await transport.SendAsync(HttpMethod.Get,
            "/rest/v1/profiles?select=status&id=" + SupabaseTransport.Equal(verifiedId) + "&limit=1",
            true, null, null, false, true, ct));
        if (first.Outcome != Outcome.Success) return new(first.Outcome);
        try { return Result<ProfileAccessState>.Ok(new(ProviderMapping.Optional(first.Value, "status"))); }
        catch (InvalidOperationException) { return new(Outcome.Malformed); }
    }
}

internal sealed class SupabaseInvitationRepository(SupabaseTransport transport) : IInvitationRepository
{
    public async Task<Result<Invitation>> FindLatestUnregisteredAsync(string email, CancellationToken ct)
    {
        var first = ProviderMapping.First(await transport.SendAsync(HttpMethod.Get,
            "/rest/v1/invitations?select=expires_at,is_registered&email=" + SupabaseTransport.Equal(email)
                + "&is_registered=eq.false&order=created_at.desc&limit=1", true, null, null, false, true, ct));
        if (first.Outcome != Outcome.Success) return new(first.Outcome);
        try
        {
            var row = first.Value;
            if (row.GetProperty("is_registered").ValueKind != JsonValueKind.False
                || !ProviderMapping.OffsetTimestamp(ProviderMapping.Required(row, "expires_at"), out var expiry))
                return new(Outcome.Malformed);
            return Result<Invitation>.Ok(new(expiry));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(Outcome.Malformed); }
    }
}

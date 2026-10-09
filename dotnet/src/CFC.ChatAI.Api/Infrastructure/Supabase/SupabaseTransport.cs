using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CFC.ChatAI.Api.Application.Models;
using CFC.ChatAI.Api.Configuration;

namespace CFC.ChatAI.Api.Infrastructure.Supabase;

internal sealed record ProviderResponse(Outcome Outcome, HttpStatusCode? Status = null, JsonElement? Body = null);

internal sealed class SupabaseTransport(IHttpClientFactory clients, SupabaseOptions options, ILogger<SupabaseTransport> logger)
{
    internal const string ClientName = "CFC.Supabase";
    internal static string Query(string value) => Uri.EscapeDataString(value);
    // PostgREST reserved-character quoting and URL encoding are separate layers.
    internal static string Equal(string value) => Query("eq.\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");

    internal async Task<ProviderResponse> SendAsync(HttpMethod method, string path, bool privileged,
        string? token, object? body, bool representation, bool parseBody, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = privileged ? options.ServiceRoleKey : options.AnonKey;
        if (string.IsNullOrWhiteSpace(key) || (!privileged && IsPrivilegedKey(key))
            || !Uri.TryCreate(options.Url, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            return Failure(Outcome.Unavailable);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(method, options.Url!.TrimEnd('/') + path);
            request.Headers.Add("apikey", key);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? key);
            if (representation) request.Headers.Add("Prefer", "return=representation");
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await clients.CreateClient(ClientName).SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Supabase returned HTTP {Status}", (int)response.StatusCode);
                return new(Outcome.Unavailable, response.StatusCode);
            }
            if (!parseBody) return new(Outcome.Success, response.StatusCode);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token),
                cancellationToken: deadline.Token);
            return new(Outcome.Success, response.StatusCode, json.RootElement.Clone());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failure(Outcome.Unavailable); }
        catch (HttpRequestException) { return Failure(Outcome.Unavailable); }
        catch (JsonException) { return Failure(Outcome.Malformed); }
        catch (FormatException) { return Failure(Outcome.Malformed); }
        catch (IOException) { return Failure(Outcome.Unavailable); }
    }

    private ProviderResponse Failure(Outcome outcome)
    {
        // Never include exceptions, URLs, bodies, identities, or submitted values.
        logger.LogWarning("Supabase operation failed: {Classification}", outcome);
        return new(outcome);
    }
    private bool IsPrivilegedKey(string key)
    {
        if (key == options.ServiceRoleKey || key.StartsWith("sb_secret_", StringComparison.Ordinal)) return true;
        try
        {
            var parts = key.Split('.');
            if (parts.Length != 3) return false;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("role", out var role) && role.GetString() == "service_role";
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException) { return false; }
    }
}

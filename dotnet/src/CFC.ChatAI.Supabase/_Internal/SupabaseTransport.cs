using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseTransport(IHttpClientFactory clients, SupabaseOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _baseUri = new(options.Url.TrimEnd('/') + "/");

    internal Uri AuthUri(string pathAndQuery = "") => new(_baseUri, "auth/v1/" + pathAndQuery);
    internal Uri RestUri(string pathAndQuery) => new(_baseUri, "rest/v1/" + pathAndQuery);
    internal Uri StorageUri(string pathAndQuery) => new(_baseUri, "storage/v1/" + pathAndQuery);

    internal static string Escape(string value) => Uri.EscapeDataString(value);

    internal async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string apiKey,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        // Credentials live only on this request. Never mutate HttpClient defaults or retain a user token.
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        HttpResponseMessage response;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            response = await clients.CreateClient("CFC.ChatAI.Supabase")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new DependencyFailureException("Supabase", "HTTP request");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyFailureException("Supabase", "HTTP request");
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            (string? SqlState, string? ConstraintName) error;
            try
            {
                error = await ReadDatabaseErrorAsync(response, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
            }
            throw new SupabaseRequestException(statusCode, error.SqlState, error.ConstraintName);
        }

        return response;
    }

    internal Task<HttpResponseMessage> SendServiceAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        SendAsync(request, options.ServiceRoleKey, options.ServiceRoleKey, cancellationToken);

    internal Task<HttpResponseMessage> SendAnonAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        SendAsync(request, options.AnonKey, options.AnonKey, cancellationToken);

    internal Task<HttpResponseMessage> SendUserAsync(
        HttpRequestMessage request,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(request, options.AnonKey, accessToken, cancellationToken);

    internal static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new InvalidDataException("Supabase returned an empty JSON response.");
    }

    internal static HttpRequestMessage JsonRequest(HttpMethod method, Uri uri, object body) =>
        new(method, uri) { Content = JsonContent.Create(body, options: JsonOptions) };

    internal static HttpRequestMessage Request(HttpMethod method, Uri uri) => new(method, uri);

    private static async Task<(string? SqlState, string? ConstraintName)> ReadDatabaseErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            var length = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = document.RootElement;
            var sqlState = root.TryGetProperty("code", out var code) ? code.GetString() : null;
            var message = root.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : null;
            var details = root.TryGetProperty("details", out var detailsValue) ? detailsValue.GetString() : null;
            var providerText = $"{message} {details}";
            const string marker = "idx_one_active_invite_per_email";
            return (sqlState, providerText.Contains(marker, StringComparison.Ordinal) ? marker : null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            return (null, null);
        }
    }
}

internal sealed class SupabaseRequestException(
    HttpStatusCode statusCode,
    string? sqlState,
    string? constraintName)
    : DependencyFailureException("Supabase", "HTTP request")
{
    internal HttpStatusCode StatusCode { get; } = statusCode;
    internal bool IsUniqueViolation => sqlState == "23505";
    internal string? ConstraintName { get; } = constraintName;
}

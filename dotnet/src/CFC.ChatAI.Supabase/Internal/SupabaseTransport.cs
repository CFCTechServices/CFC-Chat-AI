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
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient("CFC.ChatAI.Supabase")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new DependencyFailureException("Supabase", "HTTP request");
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new SupabaseRequestException(statusCode);
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
}

internal sealed class SupabaseRequestException(HttpStatusCode statusCode)
    : DependencyFailureException("Supabase", "HTTP request")
{
    internal HttpStatusCode StatusCode { get; } = statusCode;
}

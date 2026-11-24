using System.Net.Http.Headers;
using System.Text.Json;
using CFC.ChatAI.Web.Services.Admin;
using CFC.ChatAI.Web.Services.Auth;

namespace CFC.ChatAI.Web.Services.Api;

public sealed partial class ChatApiClient(HttpClient httpClient, AdminUploadPolicy? uploadPolicy = null) : IAuthenticationBackend
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    private static AuthenticationHeaderValue Bearer(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string ResourcePath(string collection, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or "..") throw new ArgumentException("An ID must identify a single resource.", nameof(id));
        return $"{collection}/{Uri.EscapeDataString(id)}";
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken,
        AuthenticationHeaderValue? authorization = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = authorization;
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ReadErrorAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The API returned null for a required response.");
    }

    private static async Task<ChatApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        JsonElement? detail = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken);
            detail = error?.Detail;
        }
        catch (JsonException)
        {
            // An upstream failure can return HTML or an empty response.
        }

        return new ChatApiException(response.StatusCode, detail);
    }

    private sealed record ApiErrorResponse(JsonElement? Detail = null);
}

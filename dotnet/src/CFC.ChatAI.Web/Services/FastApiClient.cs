using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public sealed class FastApiClient(HttpClient httpClient) : IAuthenticationBackend
{
    public Uri? BaseAddress => httpClient.BaseAddress;

    private const string SessionsPath = "api/chat/sessions";
    private const string FeedbackPath = "api/chat/feedback";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AuthConfigResponse>(HttpMethod.Get, "api/auth/config", cancellationToken);

    public Task<CheckEmailResponse> CheckEmailAsync(EmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CheckEmailResponse>(HttpMethod.Post, "api/auth/check-email", cancellationToken, body: request);

    public Task<ForgotPasswordResponse> RequestPasswordResetAsync(EmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ForgotPasswordResponse>(HttpMethod.Post, "api/auth/forgot-password", cancellationToken, body: request);

    public Task<ProfileResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileResponse>(HttpMethod.Get, "api/profile/me", cancellationToken, Bearer(accessToken));

    public Task<ProfileResponse> UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileResponse>(HttpMethod.Patch, "api/profile/me", cancellationToken, Bearer(accessToken), request);

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(
        string accessToken, bool detail = false, CancellationToken cancellationToken = default)
    {
        var path = detail ? SessionsPath + "?detail=true" : SessionsPath;
        return SendAsync<IReadOnlyList<ChatSession>>(HttpMethod.Get, path, cancellationToken, Bearer(accessToken));
    }

    public Task<ChatSession> CreateSessionAsync(
        string accessToken, CreateSessionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatSession>(HttpMethod.Post, SessionsPath, cancellationToken, Bearer(accessToken), request);

    public Task<IReadOnlyList<ChatHistoryMessage>> GetSessionHistoryAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<ChatHistoryMessage>>(HttpMethod.Get, SessionPath(sessionId), cancellationToken, Bearer(accessToken));

    public Task<ChatMessageResponse> SendMessageAsync(
        string accessToken, ChatMessageRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageResponse>(HttpMethod.Post, "api/chat/message", cancellationToken, Bearer(accessToken), request);

    public Task<ChatSession> UpdateSessionAsync(
        string accessToken, string sessionId, UpdateSessionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatSession>(HttpMethod.Patch, SessionPath(sessionId), cancellationToken, Bearer(accessToken), request);

    public Task<DeleteSessionResponse> DeleteSessionAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<DeleteSessionResponse>(HttpMethod.Delete, SessionPath(sessionId), cancellationToken, Bearer(accessToken));

    public Task<SessionFeedbackResponse> GetFeedbackAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var path = $"{FeedbackPath}?session_id={Uri.EscapeDataString(sessionId)}";
        return SendAsync<SessionFeedbackResponse>(HttpMethod.Get, path, cancellationToken, Bearer(accessToken));
    }

    public Task<SubmitFeedbackResponse> SubmitFeedbackAsync(
        string accessToken, FeedbackRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SubmitFeedbackResponse>(HttpMethod.Post, FeedbackPath, cancellationToken, Bearer(accessToken), request);

    private static string SessionPath(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (sessionId is "." or "..")
            throw new ArgumentException("A session ID must identify a single resource.", nameof(sessionId));

        return $"{SessionsPath}/{Uri.EscapeDataString(sessionId)}";
    }

    private static AuthenticationHeaderValue Bearer(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, CancellationToken cancellationToken,
        AuthenticationHeaderValue? authorization = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = authorization;
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ReadErrorAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new JsonException("FastAPI returned null for a required response.");
    }

    private static async Task<FastApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        JsonElement? detail = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<FastApiErrorResponse>(JsonOptions, cancellationToken);
            detail = error?.Detail;
        }
        catch (JsonException)
        {
            // A proxy or upstream failure can return HTML or an empty response.
        }

        return new FastApiException(response.StatusCode, detail);
    }
}

public sealed class FastApiException(HttpStatusCode statusCode, JsonElement? detail)
    : HttpRequestException(Describe(statusCode, detail), inner: null, statusCode: statusCode)
{
    public JsonElement? Detail { get; } = detail;

    private static string Describe(HttpStatusCode statusCode, JsonElement? detail)
    {
        if (detail is { ValueKind: JsonValueKind.String } message)
            return message.GetString()!;

        if (detail is { ValueKind: JsonValueKind.Array } errors)
        {
            var messages = new List<string>();
            foreach (var error in errors.EnumerateArray())
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("msg", out var text)
                    && text.ValueKind == JsonValueKind.String)
                    messages.Add(text.GetString()!);
            }
            if (messages.Count > 0)
                return string.Join("; ", messages);
        }

        return $"FastAPI returned HTTP {(int)statusCode} ({statusCode}).";
    }
}

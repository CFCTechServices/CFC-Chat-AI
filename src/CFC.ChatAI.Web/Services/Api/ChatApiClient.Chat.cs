using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services.Api;

public sealed partial class ChatApiClient
{
    private const string SessionsPath = "api/chat/sessions";
    private const string FeedbackPath = "api/chat/feedback";

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(
        string accessToken, bool detail = false, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<ChatSession>>(HttpMethod.Get, detail ? SessionsPath + "?detail=true" : SessionsPath,
            cancellationToken, Bearer(accessToken));

    public Task<ChatSession> CreateSessionAsync(
        string accessToken, CreateSessionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatSession>(HttpMethod.Post, SessionsPath, cancellationToken, Bearer(accessToken), request);

    public Task<IReadOnlyList<ChatHistoryMessage>> GetSessionHistoryAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<ChatHistoryMessage>>(HttpMethod.Get, ResourcePath(SessionsPath, sessionId), cancellationToken, Bearer(accessToken));

    public Task<ChatMessageResponse> SendMessageAsync(
        string accessToken, ChatMessageRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatMessageResponse>(HttpMethod.Post, "api/chat/message", cancellationToken, Bearer(accessToken), request);

    public Task<ChatSession> UpdateSessionAsync(
        string accessToken, string sessionId, UpdateSessionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatSession>(HttpMethod.Patch, ResourcePath(SessionsPath, sessionId), cancellationToken, Bearer(accessToken), request);

    public Task<DeleteSessionResponse> DeleteSessionAsync(
        string accessToken, string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<DeleteSessionResponse>(HttpMethod.Delete, ResourcePath(SessionsPath, sessionId), cancellationToken, Bearer(accessToken));

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

    internal async Task<Uri?> ResolveImageAsync(string token, Uri image, CancellationToken ct)
    {
        // Rebuild a relative API path; never send the user's bearer to a URL from stored content.
        if (image.IsAbsoluteUri || ChatImagePaths.ApiRoute(image.OriginalString) is not { } path) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var reply = await SendAsync<SignedImage>(HttpMethod.Get, path, timeout.Token, Bearer(token));
        return ChatImagePaths.BrowserUrl(reply.Url);
    }

    private sealed record SignedImage(string Url);
}

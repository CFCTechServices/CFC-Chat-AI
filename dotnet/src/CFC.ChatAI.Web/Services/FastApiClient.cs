using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CFC.ChatAI.Web.Models;

namespace CFC.ChatAI.Web.Services;

public sealed partial class FastApiClient(HttpClient httpClient, AdminUploadPolicy? uploadPolicy = null) : IAuthenticationBackend
{
    public Uri? BaseAddress => httpClient.BaseAddress;
    public Uri? DocsAddress => BaseAddress is null ? null : new Uri(BaseAddress, "docs");

    public async Task<bool> CanEmbedDocsAsync(Uri frontend, CancellationToken ct)
    {
        if (DocsAddress is not { } docs || docs.GetLeftPart(UriPartial.Path) == frontend.GetLeftPart(UriPartial.Path)) return false;
        if (frontend.Scheme == "https" && docs.Scheme == "http" && !docs.IsLoopback) return false;
        var origin = frontend.GetLeftPart(UriPartial.Authority);
        var sameOrigin = origin == docs.GetLeftPart(UriPartial.Authority);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await httpClient.GetAsync(docs, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/html") return false;
            if (response.Headers.TryGetValues("X-Frame-Options", out var restrictions) &&
                restrictions.Any(v => !sameOrigin || !string.Equals(v.Trim(), "SAMEORIGIN", StringComparison.OrdinalIgnoreCase))) return false;
            if (response.Headers.TryGetValues("Content-Security-Policy", out var policies))
            {
                foreach (var directive in policies.SelectMany(p => p.Split(';')))
                {
                    var parts = directive.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Equals("frame-ancestors", StringComparison.OrdinalIgnoreCase) &&
                        !parts.Skip(1).Any(v => v == "*" || (sameOrigin && v == "'self'") || v.TrimEnd('/') == origin)) return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private const string SessionsPath = "api/chat/sessions";
    private const string FeedbackPath = "api/chat/feedback";
    private const string AdminUsersPath = "api/admin/users";
    private const string DocumentsPath = "api/admin/documents";
    private const string AdminSettingsPath = "api/admin/settings";
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

    public Task<AdminUserList> GetAdminUsersAsync(string token, CancellationToken ct) =>
        SendAsync<AdminUserList>(HttpMethod.Get, AdminUsersPath, ct, Bearer(token));

    public Task<AdminSystemSettings> GetAdminSettingsAsync(string token, CancellationToken ct) =>
        SendAsync<AdminSystemSettings>(HttpMethod.Get, AdminSettingsPath, ct, Bearer(token));

    public Task<AdminSystemSettings> UpdateAdminSettingsAsync(string token, AdminSettingsPatch patch, CancellationToken ct) =>
        SendAsync<AdminSystemSettings>(HttpMethod.Patch, AdminSettingsPath, ct, Bearer(token), patch);

    public Task<ApiHealthResponse> GetHealthAsync(CancellationToken ct) =>
        SendAsync<ApiHealthResponse>(HttpMethod.Get, "api/health", ct);

    public Task<AdminInvitation> InviteUserAsync(string token, EmailRequest request, CancellationToken ct) =>
        SendAsync<AdminInvitation>(HttpMethod.Post, "api/admin/invite", ct, Bearer(token), request);

    public Task<ChangeRoleResponse> ChangeUserRoleAsync(string token, ChangeRoleRequest request, CancellationToken ct) =>
        SendAsync<ChangeRoleResponse>(HttpMethod.Post, "api/admin/change-role", ct, Bearer(token), request);

    public Task<DeactivateUserResponse> DeactivateUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<DeactivateUserResponse>(HttpMethod.Post, AdminUserPath(userId) + "/deactivate", ct, Bearer(token), new { });

    public Task<ReactivateUserResponse> ReactivateUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<ReactivateUserResponse>(HttpMethod.Post, AdminUserPath(userId) + "/reactivate", ct, Bearer(token));

    public Task<DeleteUserResponse> DeleteUserAsync(string token, string userId, CancellationToken ct) =>
        SendAsync<DeleteUserResponse>(HttpMethod.Delete, AdminUserPath(userId), ct, Bearer(token));

    private static string AdminUserPath(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (userId is "." or "..") throw new ArgumentException("Invalid user ID.", nameof(userId));
        return $"{AdminUsersPath}/{Uri.EscapeDataString(userId)}";
    }

    public Task<AdminDocumentList> GetDocumentsAsync(string token, CancellationToken ct) =>
        SendAsync<AdminDocumentList>(HttpMethod.Get, DocumentsPath, ct, Bearer(token));

    public Task<DocumentDownload> GetDocumentDownloadAsync(string token, string id, CancellationToken ct) =>
        SendAsync<DocumentDownload>(HttpMethod.Get, DocumentPath(id) + "/download", ct, Bearer(token));

    public Task<DocumentDeletion> DeleteDocumentAsync(string token, string id, CancellationToken ct) =>
        SendAsync<DocumentDeletion>(HttpMethod.Delete, DocumentPath(id), ct, Bearer(token));

    public async Task<DocumentReplacement> ReplaceDocumentAsync(string token, string id, string filename,
        Stream file, CancellationToken ct)
    {
        if (!IsWordFilename(filename)) throw new ArgumentException("Choose a .doc or .docx filename.", nameof(filename));
        using var multipart = new MultipartFormDataContent { HeaderEncodingSelector = (_, _) => System.Text.Encoding.UTF8 };
        var part = new StreamContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // Send the same UTF-8 filename as browser FormData. Python uses this value for ingestion.
        part.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"file\"; filename=\"{filename}\"");
        multipart.Add(part);
        return await SendAsync<DocumentReplacement>(HttpMethod.Put, DocumentPath(id) + "/replace", ct, Bearer(token), multipart);
    }

    internal static bool IsWordFilename(string name) => !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl) &&
        name.IndexOfAny(['/', '\\', ':', '"', '<', '>', '|', '?', '*']) < 0 &&
        !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(name)) &&
        (name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));

    private static string DocumentPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or "..") throw new ArgumentException("Invalid document ID.", nameof(id));
        return $"{DocumentsPath}/{Uri.EscapeDataString(id)}";
    }

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
        if (body is HttpContent content)
            request.Content = content;
        else if (body is not null)
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

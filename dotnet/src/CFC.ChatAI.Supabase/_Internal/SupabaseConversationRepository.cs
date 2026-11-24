using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Conversations;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseConversationRepository(SupabaseTransport transport) : IConversationRepository
{
    private const int MaximumContextMessages = 100;
    private const string SessionColumns = "id,user_id,title,created_at";
    private const string MessageColumns = "id,session_id,role,content,created_at,metadata";

    public async Task<Page<ChatSession>> ListSessionsAsync(
        string userId,
        string accessToken,
        PageRequest page,
        bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidateScope(userId, accessToken);
        if (includeDetails && page.Limit > 100)
            throw new ArgumentOutOfRangeException(nameof(page), "Detailed session pages are limited to 100 sessions.");
        var query = $"chat_sessions?select={SessionColumns}&user_id=eq.{Filter(userId)}&order=created_at.desc,id.asc&offset={page.Offset}&limit={page.Limit + 1}";
        var rows = await GetRowsAsync<SessionWire>(query, accessToken, cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > page.Limit;
        var sessions = rows.Take(page.Limit).Select(Map).ToArray();
        if (includeDetails)
        {
            for (var index = 0; index < sessions.Length; index++)
            {
                var session = sessions[index];
                var count = await GetMessageCountAsync(session.Id, accessToken, cancellationToken).ConfigureAwait(false);
                var preview = await GetLastUserPreviewAsync(session.Id, accessToken, cancellationToken).ConfigureAwait(false);
                sessions[index] = session with
                {
                    MessageCount = count,
                    LastUserMessagePreview = preview is null ? null : preview[..Math.Min(preview.Length, 100)],
                };
            }
        }

        return new Page<ChatSession>(sessions, hasMore);
    }

    public async Task<ChatSession?> CreateSessionAsync(
        string userId,
        string accessToken,
        string? title,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        using var request = SupabaseTransport.JsonRequest(
            HttpMethod.Post,
            transport.RestUri("chat_sessions?select=" + SessionColumns),
            new CreateSessionWire { UserId = userId, Title = title });
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<List<SessionWire>>(response, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<ChatSession?> GetSessionAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var query = $"chat_sessions?select={SessionColumns}&id=eq.{Filter(sessionId)}&user_id=eq.{Filter(userId)}&limit=1";
        var rows = await GetRowsAsync<SessionWire>(query, accessToken, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<ChatSession?> UpdateSessionTitleAsync(
        string userId,
        string accessToken,
        string sessionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(title);
        var query = $"chat_sessions?select={SessionColumns}&id=eq.{Filter(sessionId)}&user_id=eq.{Filter(userId)}";
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Patch, transport.RestUri(query), new { title });
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<List<SessionWire>>(response, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<bool> DeleteSessionAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (await GetSessionAsync(userId, accessToken, sessionId, cancellationToken).ConfigureAwait(false) is null)
            return false;

        var messageQuery = $"chat_messages?session_id=eq.{Filter(sessionId)}";
        using (var deleteMessages = SupabaseTransport.Request(HttpMethod.Delete, transport.RestUri(messageQuery)))
        {
            deleteMessages.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            using var response = await transport.SendUserAsync(deleteMessages, accessToken, cancellationToken).ConfigureAwait(false);
        }

        var sessionQuery = $"chat_sessions?select=id&user_id=eq.{Filter(userId)}&id=eq.{Filter(sessionId)}";
        try
        {
            using var deleteSession = SupabaseTransport.Request(HttpMethod.Delete, transport.RestUri(sessionQuery));
            deleteSession.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            using var response = await transport.SendUserAsync(deleteSession, accessToken, cancellationToken).ConfigureAwait(false);
            var deleted = await SupabaseTransport.ReadJsonAsync<List<DeletedSessionWire>>(response, cancellationToken)
                .ConfigureAwait(false);
            return deleted.Count > 0;
        }
        catch (Exception exception) when (exception is not SessionDeletionPartialFailureException)
        {
            throw new SessionDeletionPartialFailureException();
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (await GetSessionAsync(userId, accessToken, sessionId, cancellationToken).ConfigureAwait(false) is null)
            return [];
        var query = $"chat_messages?select={MessageColumns}&session_id=eq.{Filter(sessionId)}&order=created_at.asc,id.asc";
        var rows = await GetRowsAsync<MessageWire>(query, accessToken, cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetLastMessagesAsync(
        string userId,
        string accessToken,
        string sessionId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaximumContextMessages);
        if (await GetSessionAsync(userId, accessToken, sessionId, cancellationToken).ConfigureAwait(false) is null)
            return [];
        var query = $"chat_messages?select={MessageColumns}&session_id=eq.{Filter(sessionId)}&order=created_at.desc,id.desc&limit={count}";
        var rows = await GetRowsAsync<MessageWire>(query, accessToken, cancellationToken).ConfigureAwait(false);
        rows.Reverse();
        return rows.Select(Map).ToArray();
    }

    public async Task<ChatMessage?> InsertMessageAsync(
        string userId,
        string accessToken,
        CreateChatMessage message,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.SessionId);
        ArgumentNullException.ThrowIfNull(message.Content);
        if (message.Role is not ("user" or "assistant" or "system"))
            throw new ArgumentOutOfRangeException(nameof(message), "Message role must be user, assistant, or system.");
        if (await GetSessionAsync(userId, accessToken, message.SessionId, cancellationToken).ConfigureAwait(false) is null)
            return null;

        var body = new CreateMessageWire
        {
            SessionId = message.SessionId,
            Role = message.Role,
            Content = message.Content,
            Metadata = message.Metadata,
        };
        using var request = SupabaseTransport.JsonRequest(
            HttpMethod.Post,
            transport.RestUri("chat_messages?select=" + MessageColumns),
            body);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<List<MessageWire>>(response, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<FeedbackMessageContext?> GetFeedbackMessageContextAsync(
        string userId,
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(userId, accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var assistantQuery = $"chat_messages?select=id,session_id,created_at,metadata&id=eq.{Filter(messageId)}&role=eq.assistant&limit=1";
        var assistantRows = await GetRowsAsync<FeedbackMessageWire>(assistantQuery, accessToken, cancellationToken).ConfigureAwait(false);
        if (assistantRows.Count == 0) return null;
        var assistant = assistantRows[0];
        if (await GetSessionAsync(userId, accessToken, assistant.SessionId, cancellationToken).ConfigureAwait(false) is null)
            return null;

        var createdAt = ParseTimestamp(assistant.CreatedAt);
        var before = Uri.EscapeDataString(createdAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var precedingQuery = $"chat_messages?select=content&session_id=eq.{Filter(assistant.SessionId)}&role=eq.user&created_at=lt.{before}&order=created_at.desc,id.desc&limit=1";
        var precedingRows = await GetRowsAsync<ContentWire>(precedingQuery, accessToken, cancellationToken).ConfigureAwait(false);
        return new FeedbackMessageContext(
            assistant.Id,
            assistant.SessionId,
            createdAt,
            Clone(assistant.Metadata),
            precedingRows.Count == 0 ? null : precedingRows[0].Content);
    }

    private async Task<int> GetMessageCountAsync(string sessionId, string accessToken, CancellationToken cancellationToken)
    {
        var query = $"chat_messages?select=id&session_id=eq.{Filter(sessionId)}&limit=1";
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
        request.Headers.TryAddWithoutValidation("Prefer", "count=exact");
        request.Headers.TryAddWithoutValidation("Range", "0-0");
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        var contentRange = response.Content.Headers.ContentRange?.Length;
        if (contentRange.HasValue) return checked((int)contentRange.Value);
        if (response.Headers.TryGetValues("Content-Range", out var values))
        {
            var range = values.SingleOrDefault();
            if (range is not null && int.TryParse(range[(range.LastIndexOf('/') + 1)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count)) return count;
        }
        throw new InvalidDataException("Supabase did not return an exact message count.");
    }

    private async Task<string?> GetLastUserPreviewAsync(string sessionId, string accessToken, CancellationToken cancellationToken)
    {
        var query = $"chat_messages?select=content&session_id=eq.{Filter(sessionId)}&role=eq.user&order=created_at.desc,id.desc&limit=1";
        var rows = await GetRowsAsync<ContentWire>(query, accessToken, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].Content;
    }

    private async Task<List<T>> GetRowsAsync<T>(string query, string accessToken, CancellationToken cancellationToken)
    {
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
        using var response = await transport.SendUserAsync(request, accessToken, cancellationToken).ConfigureAwait(false);
        return await SupabaseTransport.ReadJsonAsync<List<T>>(response, cancellationToken).ConfigureAwait(false);
    }

    private static string Filter(string value) => Uri.EscapeDataString(value);

    private static void ValidateScope(string userId, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
    }

    private static ChatSession Map(SessionWire row) => new(
        row.Id,
        row.UserId,
        row.Title,
        ParseTimestamp(row.CreatedAt));

    private static ChatMessage Map(MessageWire row) => new(
        row.Id,
        row.SessionId,
        row.Role,
        row.Content,
        ParseTimestamp(row.CreatedAt),
        Clone(row.Metadata));

    private static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static JsonElement? Clone(JsonElement? value) => value?.Clone();

    private sealed class SessionWire
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("created_at")] public string CreatedAt { get; init; } = string.Empty;
    }

    private sealed class MessageWire
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("session_id")] public string SessionId { get; init; } = string.Empty;
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
        [JsonPropertyName("created_at")] public string CreatedAt { get; init; } = string.Empty;
        [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
    }

    private sealed class FeedbackMessageWire
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("session_id")] public string SessionId { get; init; } = string.Empty;
        [JsonPropertyName("created_at")] public string CreatedAt { get; init; } = string.Empty;
        [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
    }

    private sealed class ContentWire
    {
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
    }

    private sealed class CreateSessionWire
    {
        [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string? Title { get; init; }
    }

    private sealed class CreateMessageWire
    {
        [JsonPropertyName("session_id")] public string SessionId { get; init; } = string.Empty;
        [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
        [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
    }

    private sealed class DeletedSessionWire
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    }
}

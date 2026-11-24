using System.Text.Json;
using System.Text.Json.Serialization;
using CFC.ChatAI.Web.Serialization;

namespace CFC.ChatAI.Web.Models;

public sealed record ChatSession(
    string Id,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("created_at")] string? CreatedAt = null,
    string? Title = null,
    [property: JsonPropertyName("message_count")] int? MessageCount = null,
    [property: JsonPropertyName("last_message")] string? LastMessage = null);

public sealed record CreateSessionRequest(string? Title = "New Chat");
public sealed record UpdateSessionRequest(string Title);

public sealed record ChatMessageRequest(
    [property: JsonPropertyName("session_id")] string SessionId,
    string Content);

public sealed record ChatMessageResponse(
    string Id,
    string Role,
    string Content,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonConverter(typeof(ChatCitationsConverter))] IReadOnlyList<Dictionary<string, JsonElement>>? Citations = null,
    [property: JsonPropertyName("relevant_images"), JsonConverter(typeof(ChatImageListConverter))] IReadOnlyList<ChatImage>? RelevantImages = null);

public sealed record ChatHistoryMessage(
    string Id,
    [property: JsonPropertyName("session_id")] string SessionId,
    string Role,
    string Content,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonConverter(typeof(StoredChatMetadataConverter))] ChatMessageMetadata? Metadata = null);

public sealed record ChatMessageMetadata(
    [property: JsonConverter(typeof(ChatCitationsConverter))] IReadOnlyList<Dictionary<string, JsonElement>>? Citations = null,
    [property: JsonPropertyName("relevant_images"), JsonConverter(typeof(ChatImageListConverter))] IReadOnlyList<ChatImage>? RelevantImages = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record ChatImage(
    string? Path = null,
    int? Position = null,
    [property: JsonPropertyName("alt_text")] string? AltText = null,
    [property: JsonPropertyName("relevance_score")] double? RelevanceScore = null,
    [property: JsonPropertyName("context_text")] string? ContextText = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public sealed record FeedbackRequest(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("session_id")] string SessionId,
    int? Rating);

public sealed record SessionFeedbackResponse(IReadOnlyDictionary<string, int> Feedback);
public sealed record SubmitFeedbackResponse(bool Success, int? Score);
public sealed record DeleteSessionResponse(bool Success);

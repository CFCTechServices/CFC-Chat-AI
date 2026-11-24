using System.Text.Json;

namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>Assistant message data and its preceding user query, used above persistence for feedback event generation.</summary>
public sealed record FeedbackMessageContext(
    string MessageId,
    string SessionId,
    DateTimeOffset CreatedAt,
    JsonElement? Metadata,
    string? PreviousUserMessageContent);

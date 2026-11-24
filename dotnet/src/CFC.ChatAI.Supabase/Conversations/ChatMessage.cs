using System.Text.Json;

namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>A persisted message. Metadata is application JSON (for example citations and relevant images).</summary>
public sealed record ChatMessage(
    string Id,
    string SessionId,
    string? Role,
    string Content,
    DateTimeOffset CreatedAt,
    JsonElement? Metadata = null);

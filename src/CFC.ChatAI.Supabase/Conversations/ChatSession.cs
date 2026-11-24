namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>A persisted chat session. Detail fields are populated only by detailed list queries.</summary>
public sealed record ChatSession(
    string Id,
    string UserId,
    string? Title,
    DateTimeOffset CreatedAt,
    int? MessageCount = null,
    string? LastUserMessagePreview = null);

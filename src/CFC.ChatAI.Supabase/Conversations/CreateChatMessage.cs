using System.Text.Json;

namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>Message fields accepted for insertion; database-generated identity and timestamp are omitted.</summary>
public sealed record CreateChatMessage(
    string SessionId,
    string Role,
    string Content,
    JsonElement? Metadata = null);

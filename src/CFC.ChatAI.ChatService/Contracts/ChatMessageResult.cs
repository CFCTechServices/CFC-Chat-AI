namespace CFC.ChatAI.ChatService.Contracts;

/// <summary>An assistant message saved to the conversation, with its retrieved citations and image references.</summary>
public sealed record ChatMessageResult(
    string Id,
    string Role,
    string Content,
    IReadOnlyList<ChatCitation> Citations,
    IReadOnlyList<ChatImageReference> RelevantImages,
    DateTimeOffset CreatedAt);

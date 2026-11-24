namespace CFC.ChatAI.ChatService.Contracts;

/// <summary>Runs the authenticated, persisted chat workflow.</summary>
public interface IChatService
{
    /// <summary>
    /// Stores the user's message, retrieves and reranks relevant chunks, generates an answer, and
    /// stores the assistant reply. The supplied user ID and bearer token must come from the host's
    /// authenticated principal; they are not request-body fields.
    /// </summary>
    /// <exception cref="ChatSessionNotFoundException">The session is missing or not owned by the user.</exception>
    Task<ChatMessageResult> SendMessageAsync(
        string userId,
        string accessToken,
        string sessionId,
        string content,
        CancellationToken cancellationToken = default);
}

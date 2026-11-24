using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>Persistence operations for owned chat sessions and their messages.</summary>
public interface IConversationRepository
{
    Task<Page<ChatSession>> ListSessionsAsync(
        string userId,
        string accessToken,
        PageRequest page,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    Task<ChatSession?> CreateSessionAsync(
        string userId,
        string accessToken,
        string? title,
        CancellationToken cancellationToken = default);

    Task<ChatSession?> GetSessionAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<ChatSession?> UpdateSessionTitleAsync(
        string userId,
        string accessToken,
        string sessionId,
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes messages and then the session using two independent requests. If session deletion
    /// fails after message deletion, throws <see cref="SessionDeletionPartialFailureException"/>.
    /// This operation is not transactional.
    /// </summary>
    Task<bool> DeleteSessionAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string userId,
        string accessToken,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the last N messages in chronological order (oldest to newest).</summary>
    Task<IReadOnlyList<ChatMessage>> GetLastMessagesAsync(
        string userId,
        string accessToken,
        string sessionId,
        int count,
        CancellationToken cancellationToken = default);

    Task<ChatMessage?> InsertMessageAsync(
        string userId,
        string accessToken,
        CreateChatMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one assistant message and the immediately preceding user content for feedback processing.</summary>
    Task<FeedbackMessageContext?> GetFeedbackMessageContextAsync(
        string userId,
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default);
}

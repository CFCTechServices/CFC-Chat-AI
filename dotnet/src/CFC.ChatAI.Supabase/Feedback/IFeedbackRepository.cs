namespace CFC.ChatAI.Supabase.Feedback;

/// <summary>Persists vote and query-feedback data; reranking and embedding stay above persistence.</summary>
public interface IFeedbackRepository
{
    /// <summary>
    /// One database RPC operation. The implementation must preserve the database-side atomic
    /// vote, aggregate, and ownership semantics rather than split it into read/modify/write calls.
    /// </summary>
    Task SubmitMessageFeedbackAsync(
        FeedbackSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>Returns this user's non-null ratings for the supplied message IDs; absent/null ratings are omitted.</summary>
    Task<IReadOnlyDictionary<string, int>> GetUserScoresForMessagesAsync(
        string userId,
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>Missing feedback rows are omitted and therefore have an implicit score of zero.</summary>
    Task<IReadOnlyDictionary<string, int>> GetGlobalScoresAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, double>> GetQueryAwareScoresAsync(
        FeedbackScoreQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a batch of query events. This is not an atomic replace operation.</summary>
    Task RecordQueryEventsAsync(
        QueryFeedbackEventSet eventSet,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes events by message ID; separate from insertion and not cross-call atomic.</summary>
    Task DeleteQueryEventsForMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default);
}

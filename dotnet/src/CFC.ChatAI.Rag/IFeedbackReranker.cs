namespace CFC.ChatAI.Rag;

/// <summary>
/// Re-orders retrieved chunks using user feedback (thumbs up/down).
/// The port of FeedbackService's Phase 1 + Phase 2 re-ranking will implement this.
/// Until then, <see cref="NoOpFeedbackReranker"/> keeps Pinecone's order.
/// </summary>
public interface IFeedbackReranker
{
    /// <summary>
    /// Returns the chunks in their new order, with Rank renumbered from 1.
    /// queryVector is the embedded question (used by the query-aware Phase 2 scores).
    /// </summary>
    Task<IReadOnlyList<ContextChunk>> RerankAsync(
        IReadOnlyList<ContextChunk> chunks, float[] queryVector, CancellationToken cancellationToken = default);
}
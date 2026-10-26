namespace CFC.ChatAI.Rag;

/// <summary>
/// Leaves chunks in Pinecone's order. Used when feedback is disabled (FEEDBACK_ENABLED=false)
/// or before the FeedbackService port exists.
/// </summary>
public sealed class NoOpFeedbackReranker : IFeedbackReranker
{
    /// <summary>One shared instance; it has no state, so there's no reason to create more.</summary>
    public static NoOpFeedbackReranker Instance { get; } = new();

    public Task<IReadOnlyList<ContextChunk>> RerankAsync(
        IReadOnlyList<ContextChunk> chunks, float[] queryVector, CancellationToken cancellationToken = default) =>
        Task.FromResult(chunks);
}
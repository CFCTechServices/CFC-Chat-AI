namespace CFC.ChatAI.Pinecone.Contracts;

/// <summary>
/// A semantic search response. QueryVector is retained for query-aware feedback/reranking;
/// it is the same embedding used for the Pinecone request, not a second computation.
/// </summary>
public sealed record PineconeSearchResult(
    float[] QueryVector,
    IReadOnlyList<PineconeSearchMatch> Matches);

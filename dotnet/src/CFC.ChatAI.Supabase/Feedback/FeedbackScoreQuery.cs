namespace CFC.ChatAI.Supabase.Feedback;

/// <summary>Parameters for query-aware feedback scoring.</summary>
public sealed record FeedbackScoreQuery(
    IReadOnlyList<string> ChunkIds,
    IReadOnlyList<float> QueryEmbedding,
    double SimilarityThreshold);

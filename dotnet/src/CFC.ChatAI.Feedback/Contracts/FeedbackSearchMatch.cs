using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;

namespace CFC.ChatAI.Feedback.Contracts;

/// <summary>A vector-search match with its feedback-adjusted rank and persisted chunk, when available.</summary>
public sealed record FeedbackSearchMatch(
    int Rank,
    double AdjustedScore,
    PineconeSearchMatch SearchMatch,
    Chunk? Chunk);

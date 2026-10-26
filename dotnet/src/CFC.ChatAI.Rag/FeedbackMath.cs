namespace CFC.ChatAI.Rag;

/// <summary>
/// The feedback re-ranking formula from FeedbackService.rerank (app/core/feedback_service.py) 
/// Pure math, no network:
///    adjusted = score x (1 + alpha *tanh(net_votes / scale) + alpha_query * tanh(query_weighted / scale_query))
/// then sort by adjusted score, highest first (stable: ties keep their order), and renumber ranks. 
/// </summary>
public static class FeedbackMath
{
    /// <summary> the multiplier for one chunk. with defaults it stays between 0.55 and 1.45 </summary>
    public static double Multiplier(int netScore, double queryWeighted, FeedbackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        double globalBoost = settings.Scale != 0
            ? settings.Alpha * Math.Tanh(netScore / settings.Scale)
            : 0.0;
        
        double queryBoost = settings.ScaleQuery != 0 && queryWeighted != 0.0
            ? settings.AlphaQuery * Math.Tanh(queryWeighted / settings.ScaleQuery)
            : 0.0;
        
        return 1.0 + globalBoost + queryBoost;
    }

    /// <summary>
    /// Equivalent of Python rerank(): returns new chunks (originals untouched) sorted by adjusted
    ///  score, with AdjustedScore set, and Rank renumbered from 1. Score keeps the Pinecone value
    /// </summary>
    public static IReadOnlyList<ContextChunk> Rerank(
        IReadOnlyList<ContextChunk> chunks,
        IReadOnlyDictionary<string, int> globalScores,
        IReadOnlyDictionary<string, double> queryScores,
        FeedbackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(globalScores);
        ArgumentNullException.ThrowIfNull(queryScores);
        ArgumentNullException.ThrowIfNull(settings);

        return chunks
            .Select(chunk => chunk with
            {
                AdjustedScore = chunk.Score * Multiplier(NetScore(chunk), QueryScore(chunk), settings),
            })
            .OrderByDescending(chunk => chunk.AdjustedScore)
            .Select((chunk, i) => chunk with { Rank = i + 1})
            .ToList();
        
        int NetScore(ContextChunk chunk) =>
            chunk.ChunkId is null ? 0 : globalScores.GetValueOrDefault(chunk.ChunkId);

        double QueryScore(ContextChunk chunk) =>
            chunk.ChunkId is null ? 0.0 : queryScores.GetValueOrDefault(chunk.ChunkId);
    }
}
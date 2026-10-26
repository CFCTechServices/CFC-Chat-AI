namespace CFC.ChatAI.Rag;

/// <summary>
/// One retrieved chunk of context. C# version of teh dict built in
/// app/core/rag.py retrieve_context(); one property per dict key (18 total)
/// </summary>
public sealed record ContextChunk
{
    public required int Rank { get; init;}
    public double Score { get; init; }
    /// <summary> Score after feedback re-ranking, (Python's "adjusted_score"); null when no re-ranking was applied </summary> 
    public double? AdjustedScore { get; init;}

    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public string SourceType { get; init; } = "document";

    public string? ChunkId { get; init; }
    public string? DocId { get; init; }
    public string? SectionId { get; init; }
    public string? SectionTitle { get; init; }
    public string? SectionPath { get; init; }

    public IReadOnlyList<string> ImagePaths { get; init;} = [];
    public IReadOnlyList<string> BlockIds { get; init;} = [];

    // Video Chunks Onlly
    public double? StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public string? VideoUrl { get; init; }
    public string? TxtUrl { get; init; }
    public string? SrtUrl { get; init; }
    public string? VttUrl { get; init; }
}
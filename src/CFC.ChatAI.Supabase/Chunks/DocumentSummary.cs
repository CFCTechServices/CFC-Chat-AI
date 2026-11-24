namespace CFC.ChatAI.Supabase.Chunks;

/// <summary>A document listing projection aggregated from persisted chunk rows.</summary>
public sealed record DocumentSummary(
    string DocumentId,
    string? Source,
    string? SourceType,
    int ChunkCount);

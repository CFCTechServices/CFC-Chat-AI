namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>
/// One row of the Supabase document_chunks table (columns written by app/api/endpoints/ingest.py).
/// Property names map to snake_case JSON columns (ChunkId -> chunk_id) via the JSON options
/// in SupabaseChunkRepository.
/// </summary>
internal sealed record ChunkRow
{
    public string ChunkId { get; init; } = "";
    public string? DocId { get; init; }
    public string? SectionId { get; init; }
    public string? SectionTitle { get; init; }
    public string? SectionPath { get; init; }   // read by rag.py, but ingest never writes it
    public string? Content { get; init; }
    public string? Source { get; init; }
    public string? SourceType { get; init; }
    public IReadOnlyList<string>? ImagePaths { get; init; }

    // Video chunks only
    public double? StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public string? VideoUrl { get; init; }
    public string? TxtUrl { get; init; }
    public string? SrtUrl { get; init; }
    public string? VttUrl { get; init; }
}
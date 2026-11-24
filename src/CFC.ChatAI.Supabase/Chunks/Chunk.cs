namespace CFC.ChatAI.Supabase.Chunks;

/// <summary>
/// Searchable document content and its application metadata. It intentionally contains no
/// embedding or Pinecone-specific state; vector indexing is a separate operation.
/// </summary>
public sealed record Chunk(
    string Id,
    string DocumentId,
    string Content,
    string? SectionId = null,
    string? SectionTitle = null,
    IReadOnlyList<string>? ImagePaths = null,
    string? Source = null,
    string? SourceType = null,
    double? StartSeconds = null,
    double? EndSeconds = null,
    string? VideoUrl = null,
    string? TranscriptTextUrl = null,
    string? TranscriptSrtUrl = null,
    string? TranscriptVttUrl = null);


namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Extracted document content. Storage and indexing are separate concerns.</summary>
public sealed record DocumentProcessingResult(
    string DocumentId,
    string SourceName,
    string SourceType,
    IReadOnlyList<DocumentSection> Sections,
    IReadOnlyList<DocumentChunk> Chunks)
{
    public IReadOnlyList<DocumentImage> Images { get; init; } = [];
}

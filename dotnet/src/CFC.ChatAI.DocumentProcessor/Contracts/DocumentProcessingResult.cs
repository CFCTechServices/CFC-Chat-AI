namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Text-only output from processing a document. Storage and indexing are separate concerns.</summary>
public sealed record DocumentProcessingResult(
    string DocumentId,
    string SourceName,
    string SourceType,
    IReadOnlyList<DocumentSection> Sections,
    IReadOnlyList<DocumentChunk> Chunks);

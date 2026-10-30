namespace CFC.ChatAI.Supabase.Chunks;

/// <summary>Indicates that chunks belonging to one document disagree on non-null source metadata.</summary>
public sealed class DocumentMetadataInconsistencyException(string documentId)
    : Exception("Chunk rows contain inconsistent document source metadata.")
{
    public string DocumentId { get; } = documentId;
}

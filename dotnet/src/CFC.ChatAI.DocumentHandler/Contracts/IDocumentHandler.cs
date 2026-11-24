namespace CFC.ChatAI.DocumentHandler;

/// <summary>Coordinates text extraction, chunk persistence, vector indexing, source storage, and deletion.</summary>
public interface IDocumentHandler
{
    /// <summary>Processes and ingests one supported document or video, retaining its original in Supabase Storage.</summary>
    Task<DocumentHandlerResult> IngestAsync(
        DocumentHandlerInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Processes before purging old artifacts and replaces the original at the same key. Null indicates missing metadata.</summary>
    Task<DocumentHandlerResult?> ReplaceAsync(
        string documentId,
        DocumentHandlerInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a document's vectors, persisted chunks, and stored source/artifacts.</summary>
    Task<DocumentDeletionResult> DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>
/// Looks up chunk rows by ID. Implemented by <see cref="SupabaseChunkRepository"/>;
/// the interface lets RagPipeline be tested with a fake instead of a real database.
/// </summary>
internal interface IChunkRepository
{
    /// <summary>Rows for the given chunk IDs, keyed by chunk_id. IDs with no row are simply absent.</summary>
    Task<IReadOnlyDictionary<string, ChunkRow>> GetByIdsAsync(
        IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default);
}
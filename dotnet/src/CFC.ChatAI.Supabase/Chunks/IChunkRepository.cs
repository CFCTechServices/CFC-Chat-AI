using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Chunks;

/// <summary>Persists and hydrates full chunk content, without performing vector operations.</summary>
public interface IChunkRepository
{
    Task UpsertAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>Returns found chunks keyed by ID; absence is not a repository failure.</summary>
    Task<IReadOnlyDictionary<string, Chunk>> GetByIdsAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);

    Task<ChunkIdPage> ListIdsForDocumentAsync(
        string documentId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task DeleteByIdsAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);
}

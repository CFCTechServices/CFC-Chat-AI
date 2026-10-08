using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag;

public interface IPineconeService
{
    Task<ChunkWriteResult> UpsertChunksAsync(ChunkCollection collection, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChunkSearchMatch>> SearchAsync(ChunkCollection collection, string query, ChunkSearchOptions options, CancellationToken cancellationToken = default);
    Task<ChunkDeleteResult> DeleteChunksAsync(ChunkCollection collection, IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default);
    Task<PineconeHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

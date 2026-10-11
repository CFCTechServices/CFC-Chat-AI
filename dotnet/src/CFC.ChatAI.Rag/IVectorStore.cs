using Pinecone;

namespace CFC.ChatAI.Rag;

/// <summary>
/// Vector-level operations on one index/namespace. Implemented by <see cref="PineconeVectorStore"/>.
/// Exists so callers such as <see cref="TextVectorStore"/> can be unit-tested with a fake.
/// </summary>
public interface IVectorStore
{
    Task<IReadOnlyList<VectorMatch>> QueryAsync(
        float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null);

    Task UpsertAsync(IEnumerable<VectorRecord> records);

    Task DeleteAsync(IReadOnlyCollection<string> ids);

    Task<DescribeIndexStatsResponse> GetIndexStatsAsync();
}

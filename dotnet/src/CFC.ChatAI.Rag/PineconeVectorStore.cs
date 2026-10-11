using System.Data.Common;
using System.Numerics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Windows.Markup;
using Pinecone;
/// <summary> Alias for Pinecone.Vector to simplify usage. </summary>
using Vector = Pinecone.Vector;


namespace CFC.ChatAI.Rag;


/// <summary>A vector to store: chunk ID, embedding values, and optional metadata.</summary>
public sealed record VectorRecord(string Id, float[] Values, Metadata? Metadata = null);

/// <summary> A search result: chunk ID, similarity score, and stored metadata </summary>
public sealed record VectorMatch(string Id, float Score, Metadata? Metadata);

/// <summary>
/// C# port of app/core/vector_store.py. Wraps one pinecone index and an optional namespace
/// create one instance per index (documents and videos use seperate indexes)
/// </summary>
public sealed class PineconeVectorStore
{
    private const int UPSERT_BATCH_SIZE = 100;

    private readonly PineconeClient _client;
    private readonly string _indexName;
    private readonly string? _namespace;
    private IndexClient? _index;

    public PineconeVectorStore(string apiKey, string indexName, string? indexNamespace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        _client = new PineconeClient(apiKey);
        _indexName = indexName;
        _namespace = string.IsNullOrWhiteSpace(indexNamespace) ? null : indexNamespace;
    }

    /// <summary> Index handle created on the first use (matches Python's lazy property). </summary>
    private IndexClient Index => _index ??= _client.Index(_indexName);

    /// <summary>Equivalent of Python query(): the topK most similar vectors.</summary>
    public async Task<IReadOnlyList<VectorMatch>> QueryAsync(
        float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        var response = await Index.QueryAsync(new QueryRequest
        {
            Vector = vector,
            TopK = (uint)topK,
            IncludeMetadata = includeMetadata,
            Filter = filter,
            Namespace = _namespace,
        });

        return (response.Matches ?? [])
            .Select(m => new VectorMatch(m.Id, m.Score ?? 0f, m.Metadata))
            .ToList();
    }
    
    /// <summary> Equivalent of Python upsert_vectors(), sent in batches to stay under pinecone request limit</summary>
    public async Task UpsertAsync(IEnumerable<VectorRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var vectors = records
            .Select(r => new Vector { Id = r.Id, Values = r.Values, Metadata = r.Metadata })
            .ToList();
        
        foreach (var batch in vectors.Chunk(UPSERT_BATCH_SIZE))
        {
            await Index.UpsertAsync(new UpsertRequest { Vectors = batch, Namespace = _namespace });
        }
    }

    /// <summary> Equivalent of Python delete_document(): delete vectors by chunk ID. No-op if empty. </summary>
    public async Task DeleteAsync(IReadOnlyCollection<string> ids)
    {
        if (ids is null || ids.Count == 0) return;

        await Index.DeleteAsync(new DeleteRequest { Ids = ids.ToList(), Namespace = _namespace });
    }

    /// <summary> Equivalent of Python get_index_stats().</summary>
    public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() =>
        Index.DescribeIndexStatsAsync(new DescribeIndexStatsRequest());

}
using Pinecone;

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>A chunk to store by its text: the text is embedded and stored under <paramref name="Id"/>.</summary>
internal sealed record TextRecord(string Id, string Text, Metadata? Metadata = null);

/// <summary>
/// Search results plus the query's embedding. The vector is returned because the feedback
/// re-ranking (FeedbackService.get_query_aware_scores in Python) reuses the same query vector.
/// </summary>
internal sealed record TextSearchResult(float[] QueryVector, IReadOnlyList<VectorMatch> Matches);

/// <summary>
/// Thin text-in layer over <see cref="IVectorStore"/>: embeds with <see cref="IEmbeddingModel"/>,
/// then calls the vector store. Python has no single class for this; it reproduces two inline steps:
/// <list type="bullet">
///   <item>app/core/rag.py retrieve_context(): encode_query(query) then vector_store.query(...)</item>
///   <item>app/api/endpoints/ingest.py _prepare_vectors(): encode(chunk texts) then upsert_vectors(...)</item>
/// </list>
/// The wire format is unchanged: same record IDs, caller's metadata as-is, and the text is NOT
/// copied into metadata (Python stores minimal metadata; full text lives in Supabase document_chunks).
/// That keeps it compatible with the existing production index, with no re-ingest.
/// </summary>
internal sealed class TextVectorStore
{
    private readonly IEmbeddingModel _embedder;
    private readonly IVectorStore _store;

    public TextVectorStore(IEmbeddingModel embedder, IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        _embedder = embedder;
        _store = store;
    }

    /// <summary>The underlying vector store, for vector-level calls such as index stats.</summary>
    public IVectorStore Vectors => _store;

    /// <summary>Embed the query and return the topK most similar chunks.</summary>
    public async Task<TextSearchResult> SearchAsync(
        string query, int topK = 5, bool includeMetadata = true, Metadata? filter = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var vector = _embedder.EncodeQuery(query);
        var matches = await _store.QueryAsync(vector, topK, includeMetadata, filter);
        return new TextSearchResult(vector, matches);
    }

    /// <summary>
    /// Embed every record's text in one batch (as Python does) and upsert the vectors.
    /// No-op on an empty list. Returns the number of records written.
    /// </summary>
    public async Task<int> UpsertAsync(IReadOnlyList<TextRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return 0;

        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.Id);
            ArgumentNullException.ThrowIfNull(record.Text);
        }

        var vectors = _embedder.Encode(records.Select(r => r.Text).ToList());
        if (vectors.Count != records.Count)
            throw new InvalidOperationException(
                $"Embedding model returned {vectors.Count} vectors for {records.Count} texts.");

        await _store.UpsertAsync(records.Select((r, i) => new VectorRecord(r.Id, vectors[i], r.Metadata)));
        return records.Count;
    }

    /// <summary>Delete chunks by ID (no embedding involved). No-op if empty.</summary>
    public Task DeleteAsync(IReadOnlyCollection<string> ids) => _store.DeleteAsync(ids);
}

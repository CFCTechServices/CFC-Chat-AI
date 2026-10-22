namespace CFC.ChatAI.Pinecone;

/// <summary>Application-facing text and record operations for a configured Pinecone index.</summary>
public interface IPineconeService
{
    /// <summary>Embeds a text query and returns ordered matches together with that exact query vector.</summary>
    Task<PineconeSearchResult> SearchAsync(
        string query,
        int topK = 5,
        bool includeMetadata = true,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Embeds and upserts text records, returning the number written.</summary>
    Task<int> UpsertAsync(
        IReadOnlyList<PineconeRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes records by their Pinecone IDs. An empty collection is a no-op.</summary>
    Task DeleteAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Gets normalized statistics for the configured index.</summary>
    Task<PineconeIndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default);
}

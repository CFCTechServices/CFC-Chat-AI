namespace CFC.ChatAI.Pinecone.Contracts;

/// <summary>Embeds a query with exactly the retrieval model, without performing a vector search.</summary>
public interface IQueryEmbeddingService
{
    Task<IReadOnlyList<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);
}

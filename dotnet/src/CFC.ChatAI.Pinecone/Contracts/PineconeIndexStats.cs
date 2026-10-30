namespace CFC.ChatAI.Pinecone.Contracts;

/// <summary>Small, SDK-independent summary of a Pinecone index.</summary>
public sealed record PineconeIndexStats(
    long? TotalVectorCount,
    int? Dimension,
    IReadOnlyDictionary<string, long> NamespaceVectorCounts);

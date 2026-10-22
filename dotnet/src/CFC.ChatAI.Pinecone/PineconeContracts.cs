using System.Text.Json;

namespace CFC.ChatAI.Pinecone;

/// <summary>Application-owned text record and metadata to embed and store.</summary>
public sealed record PineconeRecord(
    string Id,
    string Text,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);

/// <summary>A semantic match. Metadata is decoded JSON and has no Pinecone SDK types.</summary>
public sealed record PineconeSearchMatch(
    string Id,
    float Score,
    IReadOnlyDictionary<string, JsonElement>? Metadata);

/// <summary>
/// A semantic search response. QueryVector is retained for query-aware feedback/reranking;
/// it is the same embedding used for the Pinecone request, not a second computation.
/// </summary>
public sealed record PineconeSearchResult(
    float[] QueryVector,
    IReadOnlyList<PineconeSearchMatch> Matches);

/// <summary>Small, SDK-independent summary of a Pinecone index.</summary>
public sealed record PineconeIndexStats(
    long? TotalVectorCount,
    int? Dimension,
    IReadOnlyDictionary<string, long> NamespaceVectorCounts);

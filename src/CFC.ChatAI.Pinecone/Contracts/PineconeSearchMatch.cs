using System.Text.Json;

namespace CFC.ChatAI.Pinecone.Contracts;

/// <summary>A semantic match. Metadata is decoded JSON and has no Pinecone SDK types.</summary>
public sealed record PineconeSearchMatch(
    string Id,
    float Score,
    IReadOnlyDictionary<string, JsonElement>? Metadata);

using System.Text.Json;

namespace CFC.ChatAI.Pinecone.Contracts;

/// <summary>Application-owned text record and metadata to embed and store.</summary>
public sealed record PineconeRecord(
    string Id,
    string Text,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);

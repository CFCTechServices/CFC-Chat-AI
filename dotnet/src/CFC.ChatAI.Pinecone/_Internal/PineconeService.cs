using System.Reflection;
using System.Text.Json;
using CFC.ChatAI.Pinecone.Contracts;
using Pinecone;

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>Single public-facing facade; embeddings, SDK models, and mapping stay internal.</summary>
internal sealed class PineconeService : IPineconeService
{
    private readonly IEmbeddingModel _embedder;
    private readonly IVectorStore _vectors;

    internal PineconeService(IEmbeddingModel embedder, IVectorStore vectors)
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
    }

    public async Task<PineconeSearchResult> SearchAsync(
        string query,
        int topK = 5,
        bool includeMetadata = true,
        IReadOnlyDictionary<string, JsonElement>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var queryVector = _embedder.EncodeQuery(query);
        var matches = await _vectors.QueryAsync(queryVector, topK, includeMetadata, ToSdkMetadata(filter))
            .WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PineconeSearchResult(queryVector, matches.Select(ToPublicMatch).ToList());
    }

    public async Task<int> UpsertAsync(
        IReadOnlyList<PineconeRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();
        if (records.Count == 0) return 0;

        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.Id);
            ArgumentNullException.ThrowIfNull(record.Text);
        }

        var embeddings = _embedder.Encode(records.Select(record => record.Text).ToList());
        if (embeddings.Count != records.Count)
            throw new InvalidOperationException(
                $"Embedding model returned {embeddings.Count} vectors for {records.Count} texts.");

        var vectors = records.Select((record, index) => new VectorRecord(
            record.Id, embeddings[index], ToSdkMetadata(record.Metadata))).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        await _vectors.UpsertAsync(vectors).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return records.Count;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();
        return DeleteCoreAsync(ids, cancellationToken);
    }

    private async Task DeleteCoreAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        await _vectors.DeleteAsync(ids).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<PineconeIndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sdkStats = await _vectors.GetIndexStatsAsync().WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var total = ReadLong(sdkStats, "TotalVectorCount");
        var dimension = ReadLong(sdkStats, "Dimension");
        var namespaces = new Dictionary<string, long>(StringComparer.Ordinal);
        var namespaceObject = ReadProperty(sdkStats, "Namespaces");
        if (namespaceObject is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string name && entry.Value is not null)
                {
                    var count = ReadLong(entry.Value, "VectorCount");
                    if (count is not null) namespaces[name] = count.Value;
                }
            }
        }

        return new PineconeIndexStats(total, dimension is null ? null : checked((int)dimension), namespaces);
    }

    private static PineconeSearchMatch ToPublicMatch(VectorMatch match)
    {
        IReadOnlyDictionary<string, JsonElement>? metadata = null;
        if (match.Metadata is not null)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(match.Metadata));
            metadata = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        }
        return new PineconeSearchMatch(match.Id, match.Score, metadata);
    }

    private static Metadata? ToSdkMetadata(IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null) return null;
        var sdkMetadata = new Metadata();
        foreach (var pair in values)
            sdkMetadata[pair.Key] = ToSdkValue(pair.Value);
        return sdkMetadata;
    }

    private static MetadataValue ToSdkValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(item => item.GetString()!).ToArray(),
        JsonValueKind.Object => ToSdkMetadata(value.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal))!,
        _ => throw new ArgumentException($"Unsupported Pinecone metadata value: {value.ValueKind}.", nameof(value)),
    };

    private static object? ReadProperty(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);

    private static long? ReadLong(object instance, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadProperty(instance, name);
            if (value is null) continue;
            try { return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
        }
        return null;
    }
}

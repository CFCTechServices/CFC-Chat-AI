using System.Text.Json;
using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace CFC.ChatAI.Rag.Tests;

internal sealed class FakeEncoder : IEmbeddingEncoder
{
    public List<string> Texts { get; } = [];
    public float[] Vector { get; set; } = UnitVector();
    public bool Ready { get; set; } = true;
    public Task<float[]> EncodeAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Texts.Add(text); return Task.FromResult(Vector);
    }
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
    public static float[] UnitVector() { var values = new float[384]; values[0] = 1; return values; }
}

internal sealed class FakeTransport : IPineconeTransport
{
    public List<(PineconeTargetOptions Target, bool Refresh)> Checks { get; } = [];
    public List<(PineconeTargetOptions Target, PineconeRecord[] Records)> Writes { get; } = [];
    public List<(PineconeTargetOptions Target, JsonElement Filter)> Deletes { get; } = [];
    public List<PineconeMatch> Matches { get; set; } = [];
    public JsonElement QueryFilter { get; private set; }
    public float[]? QueryVector { get; private set; }
    public PineconeTargetOptions? QueryTarget { get; private set; }
    public int QueryTopK { get; private set; }
    public int? FailWriteNumber { get; set; }
    public int? FailDeleteNumber { get; set; }
    public Exception? QueryFailure { get; set; }
    public Exception? ReadinessFailure { get; set; }
    public bool CancelWrite { get; set; }
    public Task EnsureReadyAsync(PineconeTargetOptions target, bool refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Checks.Add((target, refresh));
        if (ReadinessFailure is not null) throw ReadinessFailure;
        return Task.CompletedTask;
    }
    public Task UpsertAsync(PineconeTargetOptions target, IReadOnlyList<PineconeRecord> records, CancellationToken cancellationToken)
    {
        Writes.Add((target, records.ToArray()));
        if (CancelWrite) throw new OperationCanceledException();
        if (Writes.Count == FailWriteNumber) throw Failure();
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<PineconeMatch>> QueryAsync(PineconeTargetOptions target, float[] vector, int topK, JsonElement filter, CancellationToken cancellationToken)
    {
        QueryTarget = target; QueryVector = vector; QueryTopK = topK; QueryFilter = filter;
        if (QueryFailure is not null) throw QueryFailure;
        return Task.FromResult<IReadOnlyList<PineconeMatch>>(Matches);
    }
    public Task DeleteAsync(PineconeTargetOptions target, JsonElement filter, CancellationToken cancellationToken)
    {
        Deletes.Add((target, filter));
        if (Deletes.Count == FailDeleteNumber) throw Failure();
        return Task.CompletedTask;
    }
    public static PineconeServiceException Failure() => new(PineconeErrorCategory.DependencyUnavailable, "Dependency unavailable.");
}

internal sealed class ServiceFixture
{
    public FakeEncoder Encoder { get; } = new();
    public FakeTransport Transport { get; } = new();
    public EmbeddingOptions Embedding { get; } = new() { ModelPath = "not-loaded.onnx", VocabularyPath = "not-loaded.txt", ModelIdentity = "export-v1" };
    public PineconeServiceOptions Options { get; }
    public PineconeService Service { get; }
    public ServiceFixture(Func<PineconeServiceOptions, PineconeServiceOptions>? configure = null)
    {
        var options = new PineconeServiceOptions
        {
            ApiKey = "offline-test-key", Documents = Target("documents"), Videos = Target("videos"), BatchSize = 2
        };
        Options = configure?.Invoke(options) ?? options;
        Service = new(Options, Embedding, Encoder, Transport, NullLogger<PineconeService>.Instance);
    }
    public static PineconeTargetOptions Target(string name) => new() { IndexName = name, Namespace = "unit-tests", EmbeddingModelIdentity = "export-v1" };
    public static Chunk Chunk(string id = "chunk-1", ChunkCollection collection = ChunkCollection.Documents, string document = "doc-1") => new()
    {
        ChunkId = id, DocumentId = document, Text = "Full chunk about animal feed. Café 牛 🐄", Source = "Feed guide",
        SourceType = collection == ChunkCollection.Documents ? ChunkSourceType.Document : ChunkSourceType.Video
    };
    public PineconeMatch Match(Chunk chunk, double score = 0.9, ChunkCollection collection = ChunkCollection.Documents) => new(
        PineconeMetadataMapper.RecordId(collection, chunk.ChunkId), score,
        PineconeMetadataMapper.Encode(collection, chunk, Embedding.ModelIdentity, Options.MaxMetadataBytes));
}

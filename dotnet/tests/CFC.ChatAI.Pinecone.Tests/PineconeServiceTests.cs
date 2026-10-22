using System.Text.Json;
using CFC.ChatAI.Pinecone;
using CFC.ChatAI.Pinecone.Internal;
using Pinecone;

namespace CFC.ChatAI.Pinecone.Tests;

public sealed class PineconeServiceTests
{
    [Fact]
    public async Task Search_returns_matches_and_the_exact_query_vector_used()
    {
        var embedder = new FakeEmbedder();
        var vectors = new FakeVectorStore();
        var service = new PineconeService(embedder, vectors);
        var filter = new Dictionary<string, JsonElement> { ["kind"] = JsonSerializer.SerializeToElement("document") };

        var result = await service.SearchAsync("test query", topK: 3, filter: filter);

        Assert.Same(vectors.LastQueryVector, result.QueryVector);
        Assert.Equal("match-1", Assert.Single(result.Matches).Id);
        Assert.Equal(new[] { "test query" }, embedder.QueryCalls);
        Assert.Equal("document", vectors.LastFilter!["kind"].ToString());
    }

    [Fact]
    public async Task Upsert_embeds_text_preserves_ids_and_metadata()
    {
        var embedder = new FakeEmbedder();
        var vectors = new FakeVectorStore();
        var service = new PineconeService(embedder, vectors);
        var records = new[]
        {
            new PineconeRecord("chunk-1", "first", new Dictionary<string, JsonElement> { ["source"] = JsonSerializer.SerializeToElement("upload") }),
            new PineconeRecord("chunk-2", "second"),
        };

        var count = await service.UpsertAsync(records);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "first", "second" }, embedder.BatchTexts);
        Assert.Equal(new[] { "chunk-1", "chunk-2" }, vectors.Upserted!.Select(v => v.Id));
        Assert.Equal("upload", vectors.Upserted[0].Metadata!["source"].ToString());
    }

    [Fact]
    public async Task Cancellation_is_checked_before_embedding_or_io()
    {
        var embedder = new FakeEmbedder();
        var vectors = new FakeVectorStore();
        var service = new PineconeService(embedder, vectors);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAsync("query", cancellationToken: cancellation.Token));

        Assert.Empty(embedder.QueryCalls);
        Assert.Null(vectors.LastQueryVector);
    }

    private sealed class FakeEmbedder : IEmbeddingModel
    {
        public List<string> QueryCalls { get; } = [];
        public IReadOnlyList<string>? BatchTexts { get; private set; }
        public float[] EncodeQuery(string query)
        {
            QueryCalls.Add(query);
            return [1f, 2f, 3f];
        }
        public List<float[]> Encode(IReadOnlyList<string> texts)
        {
            BatchTexts = texts.ToArray();
            return texts.Select(_ => new[] { 1f, 2f, 3f }).ToList();
        }
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public float[]? LastQueryVector { get; private set; }
        public Metadata? LastFilter { get; private set; }
        public IReadOnlyList<VectorRecord>? Upserted { get; private set; }
        public IReadOnlyList<VectorMatch> Results { get; } = [new VectorMatch("match-1", .8f, null)];

        public Task<IReadOnlyList<VectorMatch>> QueryAsync(
            float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null)
        {
            LastQueryVector = vector;
            LastFilter = filter;
            return Task.FromResult(Results);
        }
        public Task UpsertAsync(IEnumerable<VectorRecord> records)
        {
            Upserted = records.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(IReadOnlyCollection<string> ids) => Task.CompletedTask;
        public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() => throw new NotSupportedException();
    }
}

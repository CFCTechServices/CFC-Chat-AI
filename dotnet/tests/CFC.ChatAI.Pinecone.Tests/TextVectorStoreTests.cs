using System.Text.Json;
using CFC.ChatAI.Pinecone.Internal;
using Pinecone;

namespace CFC.ChatAI.Pinecone.Tests;

/// <summary>
/// Unit tests for the text-in layer, using fakes (no model, no network).
/// They check the layer wires text -> embedding -> vector store correctly and changes nothing else.
/// </summary>
public class TextVectorStoreTests
{
    [Fact]
    public async Task SearchAsync_EmbedsQueryAndPassesArgumentsThrough()
    {
        var embedder = new FakeEmbedder();
        var store = new FakeVectorStore();
        var filter = new Metadata();
        var layer = new TextVectorStore(embedder, store);

        var result = await layer.SearchAsync("how do I add an ingredient?", topK: 7, includeMetadata: false, filter: filter);

        Assert.Equal(new[] { "how do I add an ingredient?" }, embedder.QueryCalls);
        var call = Assert.Single(store.QueryCalls);
        Assert.Equal(FakeEmbedder.VectorFor("how do I add an ingredient?"), call.Vector);
        Assert.Equal(7, call.TopK);
        Assert.False(call.IncludeMetadata);
        Assert.Same(filter, call.Filter);
        Assert.Same(call.Vector, result.QueryVector);       // same vector handed back for feedback re-ranking
        Assert.Same(store.QueryResult, result.Matches);
    }

    [Fact]
    public async Task UpsertAsync_EmbedsAllTextsInOneBatchAndKeepsIdsAndMetadata()
    {
        var embedder = new FakeEmbedder();
        var store = new FakeVectorStore();
        var layer = new TextVectorStore(embedder, store);
        var metaA = new Metadata();
        var records = new List<TextRecord>
        {
            new("chunk-a", "first text", metaA),
            new("chunk-b", "second text"),
        };

        var written = await layer.UpsertAsync(records);

        Assert.Equal(2, written);
        var batch = Assert.Single(embedder.BatchCalls);   // one batch, in order
        Assert.Equal(new[] { "first text", "second text" }, batch);
        var stored = Assert.Single(store.UpsertCalls);
        Assert.Equal(new[] { "chunk-a", "chunk-b" }, stored.Select(r => r.Id));
        Assert.Equal(FakeEmbedder.VectorFor("first text"), stored[0].Values);
        Assert.Equal(FakeEmbedder.VectorFor("second text"), stored[1].Values);
        Assert.Same(metaA, stored[0].Metadata);   // metadata untouched; text is not added to it
        Assert.Null(stored[1].Metadata);
    }

    [Fact]
    public async Task UpsertAsync_EmptyListIsNoOp()
    {
        var embedder = new FakeEmbedder();
        var store = new FakeVectorStore();

        var written = await new TextVectorStore(embedder, store).UpsertAsync([]);

        Assert.Equal(0, written);
        Assert.Empty(embedder.BatchCalls);
        Assert.Empty(store.UpsertCalls);
    }

    [Fact]
    public async Task UpsertAsync_BlankIdThrowsBeforeEmbeddingOrWriting()
    {
        var embedder = new FakeEmbedder();
        var store = new FakeVectorStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new TextVectorStore(embedder, store).UpsertAsync([new TextRecord(" ", "text")]));

        Assert.Empty(embedder.BatchCalls);
        Assert.Empty(store.UpsertCalls);
    }

    [Fact]
    public async Task UpsertAsync_VectorCountMismatchThrowsBeforeWriting()
    {
        var embedder = new FakeEmbedder { DropLastVector = true };
        var store = new FakeVectorStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TextVectorStore(embedder, store).UpsertAsync([new TextRecord("a", "x"), new TextRecord("b", "y")]));

        Assert.Empty(store.UpsertCalls);
    }

    [Fact]
    public async Task DeleteAsync_PassesIdsThrough()
    {
        var store = new FakeVectorStore();

        await new TextVectorStore(new FakeEmbedder(), store).DeleteAsync(["a", "b"]);

        Assert.Equal(new[] { "a", "b" }, Assert.Single(store.DeleteCalls));
    }

    // ---- fakes ----

    private sealed class FakeEmbedder : IEmbeddingModel
    {
        public List<string> QueryCalls { get; } = [];
        public List<List<string>> BatchCalls { get; } = [];
        public bool DropLastVector { get; init; }

        /// <summary>Deterministic stand-in vector derived from the text.</summary>
        public static float[] VectorFor(string text) => [text.Length, text.GetHashCode(StringComparison.Ordinal) % 1000];

        public float[] EncodeQuery(string query)
        {
            QueryCalls.Add(query);
            return VectorFor(query);
        }

        public List<float[]> Encode(IReadOnlyList<string> texts)
        {
            BatchCalls.Add(texts.ToList());
            var vectors = texts.Select(VectorFor).ToList();
            if (DropLastVector) vectors.RemoveAt(vectors.Count - 1);
            return vectors;
        }
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public record QueryCall(float[] Vector, int TopK, bool IncludeMetadata, Metadata? Filter);

        public List<QueryCall> QueryCalls { get; } = [];
        public List<List<VectorRecord>> UpsertCalls { get; } = [];
        public List<List<string>> DeleteCalls { get; } = [];
        public IReadOnlyList<VectorMatch> QueryResult { get; } = [new VectorMatch("chunk-a", 0.9f, null)];

        public Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null)
        {
            QueryCalls.Add(new QueryCall(vector, topK, includeMetadata, filter));
            return Task.FromResult(QueryResult);
        }

        public Task UpsertAsync(IEnumerable<VectorRecord> records)
        {
            UpsertCalls.Add(records.ToList());
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IReadOnlyCollection<string> ids)
        {
            DeleteCalls.Add(ids.ToList());
            return Task.CompletedTask;
        }

        public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() => throw new NotSupportedException();
    }
}

/// <summary>
/// Offline parity through the text layer with the real ONNX model: for the 15 questions in
/// retrieval_reference.json, the vector TextVectorStore sends to Pinecone must match the vector
/// Python's VectorStore was queried with. (The live check against Pinecone is
/// tools/CFC.ChatAI.Rag.VectorStoreCheck --text.)
/// </summary>
public class TextVectorStoreParityTests : IClassFixture<EmbeddingModelFixture>
{
    private const double MIN_COSINE = 0.9999;
    private readonly EmbeddingModelFixture _fixture;

    public TextVectorStoreParityTests(EmbeddingModelFixture fixture) => _fixture = fixture;

    private record ReferenceMatch(string Id, double Score);
    private record RetrievalReference(string Question, float[] QueryVector, List<ReferenceMatch> Matches);

    public static IEnumerable<object[]> Questions() =>
        LoadReferences().Select(r => new object[] { r.Question });

    private static List<RetrievalReference> LoadReferences() =>
        JsonSerializer.Deserialize<List<RetrievalReference>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "retrieval_reference.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Theory]
    [MemberData(nameof(Questions))]
    public async Task SearchAsync_SendsSameQueryVectorAsPython(string question)
    {
        var expected = LoadReferences().Single(r => r.Question == question).QueryVector;
        var store = new RecordingStore();

        var result = await new TextVectorStore(_fixture.Model, store).SearchAsync(question, topK: 5);

        Assert.Equal(expected.Length, store.LastVector!.Length);
        Assert.True(Cosine(expected, store.LastVector) >= MIN_COSINE,
            $"cosine {Cosine(expected, store.LastVector):F6} < {MIN_COSINE} for \"{question}\"");
        Assert.Same(store.LastVector, result.QueryVector);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private sealed class RecordingStore : IVectorStore
    {
        public float[]? LastVector { get; private set; }

        public Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null)
        {
            LastVector = vector;
            return Task.FromResult<IReadOnlyList<VectorMatch>>([]);
        }

        public Task UpsertAsync(IEnumerable<VectorRecord> records) => throw new NotSupportedException();
        public Task DeleteAsync(IReadOnlyCollection<string> ids) => throw new NotSupportedException();
        public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() => throw new NotSupportedException();
    }
}

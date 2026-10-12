using CFC.ChatAI.Rag;
using Pinecone;

namespace CFC.ChatAI.Rag.Tests;

/// <summary>
/// RetrieveContextAsync merge rules, tested with fakes: no model, no Pinecone, no Supabase.
/// </summary>
public class RagPipelineRetrieveTests
{
    // Builds a pipeline whose Pinecone returns `matches` and whose Supabase returns `rows`.
    private static RagPipeline Pipeline(IReadOnlyList<VectorMatch> matches, params ChunkRow[] rows) =>
        new(new TextVectorStore(new FakeEmbedder(), new FakeVectorStore(matches)), new FakeChunkRepository(rows));

    [Fact]
    public async Task SupabaseRowWinsOverPineconeMetadata()
    {
        var meta = new Metadata { ["source"] = "from-pinecone.docx", ["doc_id"] = "pinecone-doc" };
        var row = new ChunkRow { ChunkId = "c1", Content = "Full text", Source = "from-supabase.docx", DocId = "supabase-doc" };

        var chunks = await Pipeline([new VectorMatch("c1", 0.9f, meta)], row).RetrieveContextAsync("q");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Full text", chunk.Text);
        Assert.Equal("from-supabase.docx", chunk.Source);
        Assert.Equal("supabase-doc", chunk.DocId);
    }

    [Fact]
    public async Task NoRow_FallsBackToPineconeMetadata()
    {
        var meta = new Metadata { ["text"] = "Metadata text", ["source"] = "guide.docx", ["section_id"] = "s1" };

        var chunks = await Pipeline([new VectorMatch("c1", 0.9f, meta)]).RetrieveContextAsync("q");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Metadata text", chunk.Text);
        Assert.Equal("guide.docx", chunk.Source);
        Assert.Equal("s1", chunk.SectionId);
        Assert.Equal("document", chunk.SourceType);   // default when neither side has it
    }

    [Fact]
    public async Task EmptyStringInRow_FallsBackLikePythonOr()
    {
        var meta = new Metadata { ["section_title"] = "Intro" };
        var row = new ChunkRow { ChunkId = "c1", Content = "x", SectionTitle = "" };

        var chunks = await Pipeline([new VectorMatch("c1", 0.9f, meta)], row).RetrieveContextAsync("q");

        Assert.Equal("Intro", chunks[0].SectionTitle);   // C# ?? would have kept ""
    }

    [Fact]
    public async Task EmptyImageListInRow_FallsBackToMetadataList()
    {
        var meta = new Metadata { ["image_paths"] = new MetadataValue[] { "img/a.png" } };
        var row = new ChunkRow { ChunkId = "c1", Content = "x", ImagePaths = [] };

        var chunks = await Pipeline([new VectorMatch("c1", 0.9f, meta)], row).RetrieveContextAsync("q");

        Assert.Equal(new[] { "img/a.png" }, chunks[0].ImagePaths);
    }

    [Fact]
    public async Task StartSecondsZero_IsKept_DeliberateFixOfPythonBug()
    {
        var row = new ChunkRow { ChunkId = "v1", Content = "intro", SourceType = "video", StartSeconds = 0, EndSeconds = 12.5 };

        var chunks = await Pipeline([new VectorMatch("v1", 0.9f, null)], row).RetrieveContextAsync("q");

        Assert.Equal(0, chunks[0].StartSeconds);      // Python would give None here
        Assert.Equal(12.5, chunks[0].EndSeconds);
    }

    [Fact]
    public async Task RanksFollowPineconeOrder_AndScoresAreCopied()
    {
        var matches = new[] { new VectorMatch("a", 0.9f, null), new VectorMatch("b", 0.8f, null), new VectorMatch("c", 0.7f, null) };

        var chunks = await Pipeline(matches).RetrieveContextAsync("q");

        Assert.Equal(new[] { "a", "b", "c" }, chunks.Select(c => c.ChunkId));
        Assert.Equal(new[] { 1, 2, 3 }, chunks.Select(c => c.Rank));
        Assert.Equal(0.8f, chunks[1].Score, precision: 5);
    }

    [Fact]
    public async Task SupabaseFailure_FallsBackInsteadOfThrowing()
    {
        var meta = new Metadata { ["source"] = "guide.docx" };
        var pipeline = new RagPipeline(
            new TextVectorStore(new FakeEmbedder(), new FakeVectorStore([new VectorMatch("c1", 0.9f, meta)])),
            new FakeChunkRepository(throwError: true));

        var chunks = await pipeline.RetrieveContextAsync("q");

        Assert.Equal("guide.docx", Assert.Single(chunks).Source);
    }

    [Fact]
    public async Task PineconeFailure_IsRethrown()
    {
        var pipeline = new RagPipeline(
            new TextVectorStore(new FakeEmbedder(), new FakeVectorStore([], throwError: true)),
            new FakeChunkRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RetrieveContextAsync("q"));
    }

    [Fact]
    public async Task Reranker_GetsChunksAndQueryVector_AndItsOrderIsReturned()
    {
        var matches = new[] { new VectorMatch("a", 0.9f, null), new VectorMatch("b", 0.8f, null), new VectorMatch("c", 0.7f, null) };
        var reranker = new ReversingReranker();
        var pipeline = new RagPipeline(
            new TextVectorStore(new FakeEmbedder(), new FakeVectorStore(matches)),
            new FakeChunkRepository(),
            reranker);

        var chunks = await pipeline.RetrieveContextAsync("q");

        Assert.Equal(new[] { "c", "b", "a" }, chunks.Select(c => c.ChunkId));   // reranker's order wins
        Assert.Equal(new[] { 1, 2, 3 }, chunks.Select(c => c.Rank));             // ranks renumbered
        Assert.Equal(new[] { 1f, 0f }, reranker.ReceivedVector);                 // same vector Pinecone used
    }


    // ---- fakes ----

    private sealed class FakeEmbedder : IEmbeddingModel
    {
        public float[] EncodeQuery(string query) => [1f, 0f];
        public List<float[]> Encode(IReadOnlyList<string> texts) => texts.Select(_ => new[] { 1f, 0f }).ToList();
    }

    private sealed class FakeVectorStore(IReadOnlyList<VectorMatch> matches, bool throwError = false) : IVectorStore
    {
        public Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null) =>
            throwError ? throw new InvalidOperationException("Pinecone down") : Task.FromResult(matches);

        public Task UpsertAsync(IEnumerable<VectorRecord> records) => throw new NotSupportedException();
        public Task DeleteAsync(IReadOnlyCollection<string> ids) => throw new NotSupportedException();
        public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() => throw new NotSupportedException();
    }

    private sealed class FakeChunkRepository(ChunkRow[]? rows = null, bool throwError = false) : IChunkRepository
    {
        public FakeChunkRepository(params ChunkRow[] rows) : this(rows, false) { }

        public Task<IReadOnlyDictionary<string, ChunkRow>> GetByIdsAsync(
            IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default) =>
            throwError
                ? throw new HttpRequestException("Supabase down")
                : Task.FromResult<IReadOnlyDictionary<string, ChunkRow>>((rows ?? []).ToDictionary(r => r.ChunkId));
    }
        /// <summary>Stand-in for the future feedback reranker: reverses the order and renumbers ranks.</summary>
    private sealed class ReversingReranker : IFeedbackReranker
    {
        public float[]? ReceivedVector { get; private set; }

        public Task<IReadOnlyList<ContextChunk>> RerankAsync(
            IReadOnlyList<ContextChunk> chunks, float[] queryVector, CancellationToken cancellationToken = default)
        {
            ReceivedVector = queryVector;
            IReadOnlyList<ContextChunk> reversed = Enumerable.Reverse(chunks)
                .Select((chunk, i) => chunk with { Rank = i + 1 })
                .ToList();
            return Task.FromResult(reversed);
        }
    }
}
using System.Text.Json;
using CFC.ChatAI.Feedback;
using CFC.ChatAI.Feedback.Contracts;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Feedback;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Feedback.Tests;

public sealed class FeedbackServiceTests
{
    [Fact]
    public async Task Search_reranks_by_combined_feedback_and_hydrates_chunks()
    {
        var pinecone = new FakePinecone(
            Match("a", 0.80f),
            Match("b", 0.75f),
            Match("c", 0.70f));
        var chunks = new FakeChunks("a", "b", "c");
        var feedback = new FakeFeedback
        {
            GlobalScores = new Dictionary<string, int> { ["a"] = -100, ["b"] = 100 },
            QueryScores = new Dictionary<string, double> { ["c"] = 100 },
        };
        var service = CreateService(pinecone, chunks, feedback, options =>
        {
            options.GlobalScale = 1;
            options.QueryScale = 1;
        });

        var result = await service.SearchAsync("feed requirements");

        Assert.Equal(new[] { "b", "c", "a" }, result.Matches.Select(match => match.SearchMatch.Id));
        Assert.Equal(new[] { 1, 2, 3 }, result.Matches.Select(match => match.Rank));
        Assert.True(result.Matches[0].AdjustedScore > result.Matches[0].SearchMatch.Score);
        Assert.True(result.Matches[1].AdjustedScore > result.Matches[1].SearchMatch.Score);
        Assert.True(result.Matches[2].AdjustedScore < result.Matches[2].SearchMatch.Score);
        Assert.Equal("chunk b", result.Matches[0].Chunk?.Content);
        Assert.Equal(new[] { "a", "b", "c" }, feedback.LastGlobalIds);
        Assert.Equal(0.75, feedback.LastQuery?.SimilarityThreshold);
        Assert.Equal(new float[] { 0.1f, 0.2f }, feedback.LastQuery?.QueryEmbedding);
    }

    [Fact]
    public async Task Search_fails_open_when_feedback_and_chunk_hydration_fail()
    {
        var pinecone = new FakePinecone(Match("a", 0.8f), Match("b", 0.7f));
        var chunks = new FakeChunks { ThrowOnRead = true };
        var feedback = new FakeFeedback { ThrowOnRead = true };
        var service = CreateService(pinecone, chunks, feedback);

        var result = await service.SearchAsync("query");

        Assert.Equal(new[] { "a", "b" }, result.Matches.Select(match => match.SearchMatch.Id));
        Assert.All(result.Matches, match => Assert.Null(match.Chunk));
        Assert.Equal(0.8, result.Matches[0].AdjustedScore, precision: 6);
        Assert.Equal(0.7, result.Matches[1].AdjustedScore, precision: 6);
    }

    [Fact]
    public async Task Disabled_feedback_preserves_vector_order_and_skips_feedback_reads()
    {
        var pinecone = new FakePinecone(Match("a", 0.6f), Match("b", 0.9f));
        var chunks = new FakeChunks("a", "b");
        var feedback = new FakeFeedback { ThrowOnRead = true };
        var service = CreateService(pinecone, chunks, feedback, options => options.Enabled = false);

        var result = await service.SearchAsync("query");

        Assert.Equal(new[] { "a", "b" }, result.Matches.Select(match => match.SearchMatch.Id));
        Assert.Equal(0, feedback.GlobalReadCount);
        Assert.Equal(0, feedback.QueryReadCount);
        Assert.Equal("chunk a", result.Matches[0].Chunk?.Content);
    }

    [Fact]
    public async Task Empty_vector_results_do_not_query_supabase()
    {
        var pinecone = new FakePinecone();
        var chunks = new FakeChunks { ThrowOnRead = true };
        var feedback = new FakeFeedback { ThrowOnRead = true };
        var service = CreateService(pinecone, chunks, feedback);

        var result = await service.SearchAsync("query");

        Assert.Empty(result.Matches);
        Assert.Equal(0, chunks.ReadCount);
        Assert.Equal(0, feedback.GlobalReadCount);
        Assert.Equal(0, feedback.QueryReadCount);
    }

    [Fact]
    public void Registration_rejects_feedback_weights_that_can_invert_scores()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddFeedback(options =>
        {
            options.GlobalAlpha = 0.7;
            options.QueryAlpha = 0.4;
        }));
    }

    private static IFeedbackService CreateService(
        FakePinecone pinecone,
        FakeChunks chunks,
        FakeFeedback feedback,
        Action<FeedbackOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPineconeService>(pinecone);
        services.AddSingleton<IChunkRepository>(chunks);
        services.AddSingleton<IFeedbackRepository>(feedback);
        services.AddFeedback(configure);
        return services.BuildServiceProvider().GetRequiredService<IFeedbackService>();
    }

    private static PineconeSearchMatch Match(string id, float score) => new(id, score, null);

    private sealed class FakePinecone(params PineconeSearchMatch[] matches) : IPineconeService
    {
        public Task<PineconeSearchResult> SearchAsync(
            string query,
            int topK = 5,
            bool includeMetadata = true,
            IReadOnlyDictionary<string, JsonElement>? filter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PineconeSearchResult([0.1f, 0.2f], matches));

        public Task<int> UpsertAsync(IReadOnlyList<PineconeRecord> records, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PineconeIndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeChunks(params string[] chunkIds) : IChunkRepository
    {
        public int ReadCount { get; private set; }
        public bool ThrowOnRead { get; init; }

        public Task<IReadOnlyDictionary<string, Chunk>> GetByIdsAsync(
            IReadOnlyList<string> requestedIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("Chunk store unavailable.");
            IReadOnlyDictionary<string, Chunk> found = chunkIds
                .Intersect(requestedIds, StringComparer.Ordinal)
                .ToDictionary(id => id, id => new Chunk(id, "doc", $"chunk {id}"), StringComparer.Ordinal);
            return Task.FromResult(found);
        }

        public Task UpsertAsync(IReadOnlyList<Chunk> values, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChunkIdPage> ListIdsForDocumentAsync(string documentId, PageRequest page, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentMetadata?> GetDocumentMetadataAsync(string documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Page<DocumentSummary>> ListDocumentSummariesAsync(PageRequest page, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeFeedback : IFeedbackRepository
    {
        public IReadOnlyDictionary<string, int> GlobalScores { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, double> QueryScores { get; init; } = new Dictionary<string, double>();
        public bool ThrowOnRead { get; init; }
        public int GlobalReadCount { get; private set; }
        public int QueryReadCount { get; private set; }
        public IReadOnlyList<string>? LastGlobalIds { get; private set; }
        public FeedbackScoreQuery? LastQuery { get; private set; }

        public Task<IReadOnlyDictionary<string, int>> GetGlobalScoresAsync(
            IReadOnlyList<string> chunkIds,
            CancellationToken cancellationToken = default)
        {
            GlobalReadCount++;
            LastGlobalIds = chunkIds;
            if (ThrowOnRead) throw new InvalidOperationException("Feedback store unavailable.");
            return Task.FromResult(GlobalScores);
        }

        public Task<IReadOnlyDictionary<string, double>> GetQueryAwareScoresAsync(
            FeedbackScoreQuery query,
            CancellationToken cancellationToken = default)
        {
            QueryReadCount++;
            LastQuery = query;
            if (ThrowOnRead) throw new InvalidOperationException("Feedback store unavailable.");
            return Task.FromResult(QueryScores);
        }

        public Task SubmitMessageFeedbackAsync(FeedbackSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> GetUserScoresForMessagesAsync(
            string userId,
            IReadOnlyList<string> messageIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordQueryEventsAsync(QueryFeedbackEventSet eventSet, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteQueryEventsForMessageAsync(string messageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

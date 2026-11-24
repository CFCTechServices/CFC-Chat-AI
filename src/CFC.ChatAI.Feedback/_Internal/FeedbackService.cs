using CFC.ChatAI.Feedback.Contracts;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Feedback;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Feedback.Internal;

internal sealed class FeedbackService : IFeedbackService
{
    private readonly IPineconeService _pinecone;
    private readonly IChunkRepository _chunks;
    private readonly IFeedbackRepository _feedback;
    private readonly FeedbackOptions _options;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(
        IPineconeService pinecone,
        IChunkRepository chunks,
        IFeedbackRepository feedback,
        FeedbackOptions options,
        ILogger<FeedbackService> logger)
    {
        _pinecone = pinecone ?? throw new ArgumentNullException(nameof(pinecone));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FeedbackSearchResult> SearchAsync(
        string query,
        int topK = 5,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        var search = await _pinecone.SearchAsync(
            query,
            topK,
            includeMetadata: true,
            filter,
            cancellationToken).ConfigureAwait(false);

        if (search.Matches.Count == 0)
            return new FeedbackSearchResult([]);

        var chunkIds = search.Matches
            .Select(match => match.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var chunksTask = TryGetChunksAsync(chunkIds, cancellationToken);
        var globalScoresTask = _options.Enabled
            ? TryGetGlobalScoresAsync(chunkIds, cancellationToken)
            : Task.FromResult<IReadOnlyDictionary<string, int>>(EmptyScores<int>());
        var queryScoresTask = _options.Enabled
            ? TryGetQueryAwareScoresAsync(chunkIds, search.QueryVector, cancellationToken)
            : Task.FromResult<IReadOnlyDictionary<string, double>>(EmptyScores<double>());

        await Task.WhenAll(chunksTask, globalScoresTask, queryScoresTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var chunks = await chunksTask.ConfigureAwait(false);
        var globalScores = await globalScoresTask.ConfigureAwait(false);
        var queryScores = await queryScoresTask.ConfigureAwait(false);

        var scoredMatches = search.Matches
            .Select((match, index) => new
            {
                Match = match,
                OriginalIndex = index,
                AdjustedScore = AdjustScore(match, globalScores, queryScores),
            });
        var orderedMatches = _options.Enabled
            ? scoredMatches.OrderByDescending(item => item.AdjustedScore).ThenBy(item => item.OriginalIndex)
            : scoredMatches.OrderBy(item => item.OriginalIndex);
        var ranked = orderedMatches
            .Select((item, index) => new FeedbackSearchMatch(
                index + 1,
                item.AdjustedScore,
                item.Match,
                chunks.TryGetValue(item.Match.Id, out var chunk) ? chunk : null))
            .ToArray();

        return new FeedbackSearchResult(ranked);
    }

    private double AdjustScore(
        PineconeSearchMatch match,
        IReadOnlyDictionary<string, int> globalScores,
        IReadOnlyDictionary<string, double> queryScores)
    {
        var globalSignal = globalScores.TryGetValue(match.Id, out var netScore) ? netScore : 0;
        var querySignal = queryScores.TryGetValue(match.Id, out var weightedScore) ? weightedScore : 0;

        var globalBoost = _options.GlobalAlpha * Math.Tanh(globalSignal / _options.GlobalScale);
        var queryBoost = _options.QueryAlpha * Math.Tanh(querySignal / _options.QueryScale);
        return match.Score * (1.0 + globalBoost + queryBoost);
    }

    private async Task<IReadOnlyDictionary<string, Chunk>> TryGetChunksAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0)
            return new Dictionary<string, Chunk>(StringComparer.Ordinal);

        try
        {
            return await _chunks.GetByIdsAsync(chunkIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not hydrate search results from Supabase; returning vector matches without chunk data.");
            return new Dictionary<string, Chunk>(StringComparer.Ordinal);
        }
    }

    private async Task<IReadOnlyDictionary<string, int>> TryGetGlobalScoresAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0)
            return EmptyScores<int>();

        try
        {
            return await _feedback.GetGlobalScoresAsync(chunkIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not read global feedback scores; continuing without global reranking.");
            return EmptyScores<int>();
        }
    }

    private async Task<IReadOnlyDictionary<string, double>> TryGetQueryAwareScoresAsync(
        IReadOnlyList<string> chunkIds,
        IReadOnlyList<float> queryEmbedding,
        CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0 || queryEmbedding.Count == 0)
            return EmptyScores<double>();

        try
        {
            var query = new FeedbackScoreQuery(chunkIds, queryEmbedding, _options.SimilarityThreshold);
            return await _feedback.GetQueryAwareScoresAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not read query-aware feedback scores; continuing without query-aware reranking.");
            return EmptyScores<double>();
        }
    }

    private static IReadOnlyDictionary<string, T> EmptyScores<T>() =>
        new Dictionary<string, T>(StringComparer.Ordinal);
}

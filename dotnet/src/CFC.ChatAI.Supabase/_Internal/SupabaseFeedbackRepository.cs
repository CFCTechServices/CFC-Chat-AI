using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Feedback;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseFeedbackRepository(SupabaseTransport transport) : IFeedbackRepository
{
    private const int FilterBatchSize = 100;

    public async Task SubmitMessageFeedbackAsync(
        FeedbackSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.MessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.UserId);
        if (submission.Rating.HasValue && submission.Rating.Value != -1 && submission.Rating.Value != 1)
            throw new ArgumentOutOfRangeException(nameof(submission), "Rating must be -1, 1, or null.");

        var uri = transport.RestUri("rpc/submit_message_feedback");
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, new
        {
            p_message_id = submission.MessageId,
            p_user_id = submission.UserId,
            p_new_rating = submission.Rating,
        });
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetUserScoresForMessagesAsync(
        string userId,
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count > 1000)
            throw new ArgumentOutOfRangeException(nameof(messageIds), "At most 1000 message IDs may be queried at once.");

        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var batch in messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Chunk(FilterBatchSize))
        {
            var quoted = string.Join(',', batch.Select(id =>
                $"\"{id.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
            var query = $"feedback?select=message_id,score&user_id=eq.{SupabaseTransport.Escape(userId)}&message_id={SupabaseTransport.Escape($"in.({quoted})")}&score=not.is.null";
            using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            var rows = await SupabaseTransport.ReadJsonAsync<List<UserFeedbackRow>>(response, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (row.Score.HasValue) scores[row.MessageId] = row.Score.Value;
            }
        }

        return scores;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetGlobalScoresAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var batch in chunkIds.Distinct(StringComparer.Ordinal).Chunk(FilterBatchSize))
        {
            if (batch.Length == 0) continue;
            var quoted = string.Join(',', batch.Select(id =>
                $"\"{id.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
            var query = $"chunk_feedback_scores?select=chunk_id,net_score&chunk_id={SupabaseTransport.Escape($"in.({quoted})")}";
            using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            var rows = await SupabaseTransport.ReadJsonAsync<List<GlobalScoreRow>>(response, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows) scores[row.ChunkId] = row.NetScore;
        }

        return scores;
    }

    public async Task<IReadOnlyDictionary<string, double>> GetQueryAwareScoresAsync(
        FeedbackScoreQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.ChunkIds);
        ArgumentNullException.ThrowIfNull(query.QueryEmbedding);
        if (query.ChunkIds.Count == 0 || query.QueryEmbedding.Count == 0)
            return new Dictionary<string, double>();
        if (query.SimilarityThreshold is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(query), "Cosine similarity threshold must be in [-1, 1].");

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var batch in query.ChunkIds.Distinct(StringComparer.Ordinal).Chunk(FilterBatchSize))
        {
            var uri = transport.RestUri("rpc/get_query_aware_chunk_scores");
            using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, new
            {
                p_query_emb = query.QueryEmbedding,
                p_chunk_ids = batch,
                p_sim_threshold = query.SimilarityThreshold,
            });
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            var rows = await SupabaseTransport.ReadJsonAsync<List<QueryScoreRow>>(response, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows) scores[row.ChunkId] = row.WeightedScore;
        }

        return scores;
    }

    public async Task RecordQueryEventsAsync(
        QueryFeedbackEventSet eventSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventSet);
        ArgumentNullException.ThrowIfNull(eventSet.Events);
        ArgumentNullException.ThrowIfNull(eventSet.QueryEmbedding);
        if (eventSet.QueryEmbedding.Count == 0 && eventSet.Events.Count > 0)
            throw new ArgumentException("A query embedding is required when recording feedback events.", nameof(eventSet));
        foreach (var item in eventSet.Events)
        {
            if (item.Rating != -1 && item.Rating != 1)
                throw new ArgumentException("Event ratings must be -1 or 1.", nameof(eventSet));
        }

        foreach (var batch in eventSet.Events.Chunk(WriteBatchSize))
        {
            var rows = batch.Select(item => new QueryEventRow
            {
                ChunkId = item.ChunkId,
                MessageId = item.MessageId,
                Rating = item.Rating,
                QueryEmbedding = eventSet.QueryEmbedding,
            }).ToArray();
            using var request = SupabaseTransport.JsonRequest(
                HttpMethod.Post, transport.RestUri("chunk_feedback_events"), rows);
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteQueryEventsForMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var query = $"chunk_feedback_events?message_id=eq.{SupabaseTransport.Escape(messageId)}";
        using var request = SupabaseTransport.Request(HttpMethod.Delete, transport.RestUri(query));
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private const int WriteBatchSize = 500;

    private sealed class UserFeedbackRow
    {
        [JsonPropertyName("message_id")] public string MessageId { get; init; } = string.Empty;
        [JsonPropertyName("score")] public int? Score { get; init; }
    }

    private sealed class GlobalScoreRow
    {
        [JsonPropertyName("chunk_id")] public string ChunkId { get; init; } = string.Empty;
        [JsonPropertyName("net_score")] public int NetScore { get; init; }
    }

    private sealed class QueryScoreRow
    {
        [JsonPropertyName("chunk_id")] public string ChunkId { get; init; } = string.Empty;
        [JsonPropertyName("weighted_score")] public double WeightedScore { get; init; }
    }

    private sealed class QueryEventRow
    {
        [JsonPropertyName("chunk_id")] public string ChunkId { get; init; } = string.Empty;
        [JsonPropertyName("message_id")] public string MessageId { get; init; } = string.Empty;
        [JsonPropertyName("rating")] public int Rating { get; init; }
        [JsonPropertyName("query_embedding")] public IReadOnlyList<float> QueryEmbedding { get; init; } = [];
    }
}

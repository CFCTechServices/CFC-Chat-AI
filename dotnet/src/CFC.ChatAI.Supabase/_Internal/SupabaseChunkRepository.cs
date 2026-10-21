using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseChunkRepository(SupabaseTransport transport) : IChunkRepository
{
    private const string Table = "document_chunks";
    private const int WriteBatchSize = 500;
    private const int FilterBatchSize = 100;

    public async Task UpsertAsync(
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        foreach (var chunk in chunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            ArgumentException.ThrowIfNullOrWhiteSpace(chunk.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(chunk.DocumentId);
            ArgumentNullException.ThrowIfNull(chunk.Content);
        }

        foreach (var batch in chunks.Chunk(WriteBatchSize))
        {
            var uri = transport.RestUri($"{Table}?on_conflict=chunk_id");
            using var request = SupabaseTransport.JsonRequest(
                HttpMethod.Post, uri, batch.Select(ChunkRow.FromDomain).ToArray());
            request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<string, Chunk>> GetByIdsAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var chunks = new Dictionary<string, Chunk>(StringComparer.Ordinal);
        foreach (var batch in chunkIds.Distinct(StringComparer.Ordinal).Chunk(FilterBatchSize))
        {
            if (batch.Length == 0) continue;
            var query = $"{Table}?select=*&chunk_id={InFilter(batch)}";
            using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            var rows = await SupabaseTransport.ReadJsonAsync<List<ChunkRow>>(response, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                var chunk = row.ToDomain();
                chunks[chunk.Id] = chunk;
            }
        }

        return chunks;
    }

    public async Task<ChunkIdPage> ListIdsForDocumentAsync(
        string documentId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(page);
        var query = $"{Table}?select=chunk_id&doc_id=eq.{SupabaseTransport.Escape(documentId)}" +
                    $"&order=chunk_id.asc&offset={page.Offset}&limit={page.Limit}";
        using var request = SupabaseTransport.Request(HttpMethod.Get, transport.RestUri(query));
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<List<ChunkIdRow>>(response, cancellationToken)
            .ConfigureAwait(false);
        return new ChunkIdPage(rows.Select(row => row.ChunkId).ToArray(), rows.Count == page.Limit);
    }

    public async Task DeleteByIdsAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        foreach (var batch in chunkIds.Distinct(StringComparer.Ordinal).Chunk(FilterBatchSize))
        {
            if (batch.Length == 0) continue;
            var query = $"{Table}?chunk_id={InFilter(batch)}";
            using var request = SupabaseTransport.Request(HttpMethod.Delete, transport.RestUri(query));
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string InFilter(IEnumerable<string> values)
    {
        var quoted = string.Join(',', values.Select(value =>
            $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
        return SupabaseTransport.Escape($"in.({quoted})");
    }

    private sealed class ChunkIdRow
    {
        [JsonPropertyName("chunk_id")] public string ChunkId { get; init; } = string.Empty;
    }

    private sealed class ChunkRow
    {
        [JsonPropertyName("chunk_id")] public string ChunkId { get; init; } = string.Empty;
        [JsonPropertyName("doc_id")] public string DocumentId { get; init; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
        [JsonPropertyName("section_id")] public string? SectionId { get; init; }
        [JsonPropertyName("section_title")] public string? SectionTitle { get; init; }
        [JsonPropertyName("image_paths")] public List<string>? ImagePaths { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("source_type")] public string? SourceType { get; init; }
        [JsonPropertyName("start_seconds")] public double? StartSeconds { get; init; }
        [JsonPropertyName("end_seconds")] public double? EndSeconds { get; init; }
        [JsonPropertyName("video_url")] public string? VideoUrl { get; init; }
        [JsonPropertyName("txt_url")] public string? TranscriptTextUrl { get; init; }
        [JsonPropertyName("srt_url")] public string? TranscriptSrtUrl { get; init; }
        [JsonPropertyName("vtt_url")] public string? TranscriptVttUrl { get; init; }

        internal static ChunkRow FromDomain(Chunk chunk) => new()
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            Content = chunk.Content,
            SectionId = chunk.SectionId,
            SectionTitle = chunk.SectionTitle,
            ImagePaths = chunk.ImagePaths?.ToList(),
            Source = chunk.Source,
            SourceType = chunk.SourceType,
            StartSeconds = chunk.StartSeconds,
            EndSeconds = chunk.EndSeconds,
            VideoUrl = chunk.VideoUrl,
            TranscriptTextUrl = chunk.TranscriptTextUrl,
            TranscriptSrtUrl = chunk.TranscriptSrtUrl,
            TranscriptVttUrl = chunk.TranscriptVttUrl,
        };

        internal Chunk ToDomain() => new(
            ChunkId,
            DocumentId,
            Content,
            SectionId,
            SectionTitle,
            ImagePaths,
            Source,
            SourceType,
            StartSeconds,
            EndSeconds,
            VideoUrl,
            TranscriptTextUrl,
            TranscriptSrtUrl,
            TranscriptVttUrl);
    }
}

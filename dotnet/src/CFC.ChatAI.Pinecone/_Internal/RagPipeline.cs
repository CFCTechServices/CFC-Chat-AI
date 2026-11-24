using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pinecone;

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>C# port of app/core/rag.py RAGPipeline.</summary>
internal sealed class RagPipeline
{
    /// <summary>Mirrors settings.DEFAULT_TOP_K in app/config.py.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Mirrors settings.MAX_CONTEXT_LENGTH in app/config.py.</summary>
    public const int DefaultMaxContextLength = 4000;

    private readonly TextVectorStore _textStore;
    private readonly IChunkRepository _chunks;
    private readonly ILogger<RagPipeline> _logger;
    private readonly IFeedbackReranker _reranker;

    public RagPipeline(TextVectorStore textStore, IChunkRepository chunks, IFeedbackReranker? reranker = null, ILogger<RagPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(textStore);
        ArgumentNullException.ThrowIfNull(chunks);

        _textStore = textStore;
        _chunks = chunks;
        _reranker = reranker ?? NoOpFeedbackReranker.Instance;
        _logger = logger ?? NullLogger<RagPipeline>.Instance;
    }

    /// <summary>
    /// Equivalent of Python retrieve_context(): find the closest chunks for a question and
    /// fill in their full text from Supabase, falling back to Pinecone metadata.
    /// </summary>
    public async Task<IReadOnlyList<ContextChunk>> RetrieveContextAsync(
        string query, int topK = DefaultTopK, Metadata? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            // 1) Embed the question and find the closest chunks in Pinecone.
            var search = await _textStore.SearchAsync(query, topK, includeMetadata: true, filter);

            // 2) Fetch the full rows (with the text) from Supabase.
            var ids = search.Matches.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var rows = await GetRowsOrEmptyAsync(ids, cancellationToken);

            // 3) Merge each match with its row. Rank follows Pinecone's order, starting at 1.
            var chunks = new List<ContextChunk>(search.Matches.Count);
            for (int i = 0; i < search.Matches.Count; i++)
            {
                var match = search.Matches[i];
                var row = string.IsNullOrEmpty(match.Id) ? null : rows.GetValueOrDefault(match.Id);
                chunks.Add(Merge(i + 1, match, row));
            }

            // 4) Feedback re-ranking (a no-op unless a real reranker was passed in).
            return await _reranker.RerankAsync(chunks, search.QueryVector, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve context for query");
            throw;
        }
    }

    /// <summary>
    /// Python swallows a Supabase failure and carries on with Pinecone metadata only.
    /// We keep that fallback, but log a warning so the failure isn't invisible.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ChunkRow>> GetRowsOrEmptyAsync(
        List<string> ids, CancellationToken cancellationToken)
    {
        try
        {
            return await _chunks.GetByIdsAsync(ids, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Supabase chunk lookup failed for {Count} chunks; using Pinecone metadata only", ids.Count);
            return new Dictionary<string, ChunkRow>();
        }
    }

    /// <summary>Builds one ContextChunk: the Supabase row wins, Pinecone metadata is the fallback.</summary>
    private static ContextChunk Merge(int rank, VectorMatch match, ChunkRow? row)
    {
        var meta = match.Metadata;

        return new ContextChunk
        {
            Rank = rank,
            Score = match.Score,
            ChunkId = match.Id,

            Text = Or(Or(row?.Content, MetaString(meta, "content")), MetaString(meta, "text") ?? "") ?? "",
            Source = Or(row?.Source, MetaString(meta, "source") ?? "") ?? "",
            SourceType = Or(row?.SourceType, MetaString(meta, "source_type") ?? "document") ?? "document",

            DocId = Or(row?.DocId, MetaString(meta, "doc_id")),
            SectionId = Or(row?.SectionId, MetaString(meta, "section_id")),
            SectionTitle = Or(row?.SectionTitle, MetaString(meta, "section_title")),
            SectionPath = Or(row?.SectionPath, MetaString(meta, "section_path")),

            ImagePaths = OrList(row?.ImagePaths, MetaList(meta, "image_paths")) ?? [],
            BlockIds = MetaList(meta, "block_ids") ?? [],   // Python reads this from metadata only

            // Deliberate fix: Python's `or` treats 0 as missing, so a clip starting at 0s lost its start time.
            StartSeconds = row?.StartSeconds ?? MetaNumber(meta, "start_seconds"),
            EndSeconds = row?.EndSeconds ?? MetaNumber(meta, "end_seconds"),

            VideoUrl = Or(row?.VideoUrl, MetaString(meta, "video_url")),
            TxtUrl = Or(row?.TxtUrl, MetaString(meta, "txt_url")),
            SrtUrl = Or(row?.SrtUrl, MetaString(meta, "srt_url")),
            VttUrl = Or(row?.VttUrl, MetaString(meta, "vtt_url")),
        };
    }

    // ---- Python-`or` helpers ----

    /// <summary>Python's `a or b` for strings: use a unless it's None or "", otherwise b (even if b is empty too).</summary>
    private static string? Or(string? a, string? b) => string.IsNullOrEmpty(a) ? b : a;

    /// <summary>Python's `a or b` for lists: an empty list counts as missing.</summary>
    private static IReadOnlyList<string>? OrList(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is { Count: > 0 } ? a : b;

    // ---- Reading Pinecone metadata ----

    private static object? MetaRaw(Metadata? meta, string key) =>
        meta is not null && meta.TryGetValue(key, out var value) ? value?.Value : null;

    private static string? MetaString(Metadata? meta, string key) => MetaRaw(meta, key) as string;

    private static double? MetaNumber(Metadata? meta, string key) => MetaRaw(meta, key) switch
    {
        double d => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static IReadOnlyList<string>? MetaList(Metadata? meta, string key) =>
        MetaRaw(meta, key) is IEnumerable<MetadataValue?> items
            ? items.Select(item => item?.Value as string).OfType<string>().ToList()
            : null;

    // ---- Formatting (Step 2, unchanged) ----

    /// <summary>
    /// Equivalent of Python format_context(): joins chunks into one string for the LLM.
    /// Each chunk is prefixed with [CHUNK_ID: ...] so the LLM can cite it.
    /// Stops at the first chunk that would exceed maxLength (separators are not counted).
    /// Note: C# string.Length counts UTF-16 units, Python len() counts code points; they only
    /// differ for emoji/rare symbols, so the cutoff can differ in that edge case.
    /// </summary>
    public static string FormatContext(
        IReadOnlyList<ContextChunk> chunks, int maxLength = DefaultMaxContextLength)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var parts = new List<string>();
        int totalLength = 0;

        foreach (var chunk in chunks)
        {
            string chunkId = chunk.ChunkId ?? "unknown";
            string titleLine = string.IsNullOrEmpty(chunk.SectionTitle)
                ? ""
                : $"Title: {chunk.SectionTitle}\n";

            string chunkText = $"[CHUNK_ID: {chunkId}]\nSource: {chunk.Source}\n{titleLine}{chunk.Text}\n";

            if (totalLength + chunkText.Length > maxLength)
                break;

            parts.Add(chunkText);
            totalLength += chunkText.Length;
        }

        return string.Join("\n---\n", parts);
    }
}
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>
/// Reads document_chunks rows through Supabase's REST API (PostgREST).
/// Equivalent of the Python call in rag.py:
///   supabase.table("document_chunks").select("*").in_("chunk_id", chunk_ids).execute()
/// </summary>
internal sealed class SupabaseChunkRepository : IChunkRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly string _restUrl;
    private readonly string _serviceRoleKey;

    public SupabaseChunkRepository(HttpClient http, string supabaseUrl, string serviceRoleKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(supabaseUrl);
        ArgumentNullException.ThrowIfNull(serviceRoleKey);

        _http = http;
        _restUrl = supabaseUrl.TrimEnd('/') + "/rest/v1/";
        _serviceRoleKey = serviceRoleKey;
    }

     public async Task<IReadOnlyDictionary<string, ChunkRow>> GetByIdsAsync(
        IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        if (chunkIds.Count == 0) return new Dictionary<string, ChunkRow>();

        using var request = new HttpRequestMessage(HttpMethod.Get, _restUrl + BuildQuery(chunkIds));
        request.Headers.Add("apikey", _serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<List<ChunkRow>>(JsonOptions, cancellationToken) ?? [];

        // Same as Python's {r["chunk_id"]: r for r in rows}: a later duplicate overwrites an earlier one.
        var byId = new Dictionary<string, ChunkRow>();
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.ChunkId))
                byId[row.ChunkId] = row;
        }
        return byId;
    }

    /// <summary>
    /// PostgREST query for "chunk_id IN (...)": document_chunks?select=*&amp;chunk_id=in.("id1","id2").
    /// Each ID is double-quoted so commas or parentheses inside an ID can't break the list.
    /// </summary>
    public static string BuildQuery(IEnumerable<string> chunkIds)
    {
        var quoted = chunkIds.Select(id => "\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
        return "document_chunks?select=*&chunk_id=in.(" + Uri.EscapeDataString(string.Join(",", quoted)) + ")";
    }
}
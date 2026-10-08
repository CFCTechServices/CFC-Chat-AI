using System.Net.Http.Headers;
using System.Text.Json;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Contracts;
using Microsoft.ML.OnnxRuntime;

namespace CFC.ChatAI.Api.Services;

public sealed class RuntimeDiagnostics(IHttpClientFactory clients, RuntimeOptions options)
{
    public const string HttpClientName = "CFC.Diagnostics";

    public AuthConfigResponse GetAuthConfig() => new(options.SupabaseUrl, PublicAnonKey());

    private string? PublicAnonKey()
    {
        var key = options.SupabaseAnonKey;
        // Protect against common deployment mistakes; never fall back to the service-role key.
        if (key is null || key == options.SupabaseServiceRoleKey || key.StartsWith("sb_secret_", StringComparison.Ordinal)) return null;
        var parts = key.Split('.');
        if (parts.Length == 3)
        {
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
                if (json.RootElement.TryGetProperty("role", out var role) && role.GetString() == "service_role") return null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException) { return null; }
        }
        return key;
    }

    public async Task<DetailedHealthResponse> GetHealthAsync(string version, CancellationToken cancellationToken)
    {
        var pinecone = CheckAsync(!string.IsNullOrEmpty(options.PineconeApiKey),
            "PINECONE_API_KEY not set", "Pinecone", async ct =>
            {
                using var response = await SendAsync("https://api.pinecone.io/indexes", HttpMethod.Get, true, ct);
                return new ServiceStatus("ok", Index: options.PineconeIndexName);
            }, cancellationToken);
        var supabase = CheckAsync(!string.IsNullOrEmpty(options.SupabaseUrl) && !string.IsNullOrEmpty(options.SupabaseServiceRoleKey),
            "SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY not set", "Supabase", async ct =>
            {
                var url = SafeServiceUrl(options.SupabaseUrl!);
                using var response = await SendAsync(url.TrimEnd('/') + "/rest/v1/profiles?select=id&limit=1", HttpMethod.Get, false, ct);
                return new ServiceStatus("ok", Url: options.SupabaseUrl);
            }, cancellationToken);
        var services = new Dictionary<string, ServiceStatus>
        {
            ["pinecone"] = await pinecone,
            ["supabase"] = await supabase,
            ["azure_openai"] = string.IsNullOrEmpty(options.AzureApiKey) || string.IsNullOrEmpty(options.AzureEndpoint)
                ? new("not_configured", "AZURE_OPENAI_API_KEY or AZURE_OPENAI_ENDPOINT not set")
                : string.IsNullOrEmpty(options.AzureDeployment)
                    ? new("degraded", "AZURE_OPENAI_DEPLOYMENT not set — LLM disabled")
                    : new("ok", Deployment: options.AzureDeployment),
            ["embedding_model"] = CheckEmbeddingAvailability()
        };
        var ok = services.Values.All(s => s.Status == "ok");
        return new(ok, version, ok ? "All systems operational (C#)" : "One or more services degraded", services);
    }

    private ServiceStatus CheckEmbeddingAvailability()
    {
        // Python only imports sentence-transformers; it does not instantiate/download a model.
        // The C# counterpart checks the deployed ONNX runtime (including its native library).
        try
        {
            _ = OrtEnv.Instance();
            return new("ok", Model: options.EmbeddingModelName);
        }
        catch { return new("error", "ONNX Runtime not available"); }
    }

    private async Task<ServiceStatus> CheckAsync(bool configured, string missing, string name,
        Func<CancellationToken, Task<ServiceStatus>> check, CancellationToken cancellationToken)
    {
        if (!configured) return new("not_configured", missing);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        try { return await check(timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new("error", name + " health check timed out"); }
        // Neither upstream bodies nor exception messages are safe on a public diagnostic route.
        catch { return new("error", name + " health check failed"); }
    }

    public async Task<VectorStoreStatsResponse> GetVectorStatsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        return await FetchVectorStatsAsync(timeout.Token).WaitAsync(timeout.Token);
    }

    private async Task<VectorStoreStatsResponse> FetchVectorStatsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.PineconeApiKey)) throw new InvalidOperationException("Pinecone is not configured.");
        // Never create an index from a public visibility request. Discover the existing document index.
        using var description = await SendAsync("https://api.pinecone.io/indexes/" + Uri.EscapeDataString(options.PineconeIndexName), HttpMethod.Get, true, cancellationToken);
        var host = description.RootElement.GetProperty("host").GetString()!;
        var url = SafeServiceUrl(host.Contains("://") ? host : "https://" + host);
        var uri = new Uri(url);
        if (uri.Port != 443 || !uri.Host.EndsWith(".pinecone.io", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath != "/")
            throw new InvalidOperationException("Invalid Pinecone host.");
        using var stats = await SendAsync(url.TrimEnd('/') + "/describe_index_stats", HttpMethod.Post, true, cancellationToken);
        return MapVectorStats(stats.RootElement, options.PineconeIndexName);
    }

    public static VectorStoreStatsResponse MapVectorStats(JsonElement stats, string indexName)
    {
        var namespaces = new List<NamespaceStats>();
        if (stats.TryGetProperty("namespaces", out var ns) && ns.ValueKind != JsonValueKind.Null)
            foreach (var entry in ns.EnumerateObject())
                namespaces.Add(new(entry.Name.Length == 0 ? "default" : entry.Name,
                    OptionalLong(entry.Value, "vectorCount") ?? 0));
        var total = OptionalLong(stats, "total_vector_count") ?? OptionalLong(stats, "totalVectorCount") ?? namespaces.Sum(n => n.VectorCount);
        var fullness = OptionalDouble(stats, "indexFullness");
        // Match Python's `camel or snake` expression, including zero/null behavior.
        if (fullness is null or 0) fullness = OptionalDouble(stats, "index_fullness");
        return new(true, indexName, total, stats.TryGetProperty("dimension", out var dim) && dim.ValueKind != JsonValueKind.Null ? dim.GetInt32() : null,
            fullness, namespaces);
    }

    private static long? OptionalLong(JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetInt64() : null;
    private static double? OptionalDouble(JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetDouble() : null;

    private static string SafeServiceUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Invalid service URL.");
        return uri.AbsoluteUri;
    }

    private async Task<JsonDocument> SendAsync(string url, HttpMethod method, bool pinecone, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, url);
        if (pinecone)
        {
            request.Headers.Add("Api-Key", options.PineconeApiKey);
            request.Headers.Add("X-Pinecone-API-Version", "2025-10");
        }
        else
        {
            request.Headers.Add("apikey", options.SupabaseServiceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SupabaseServiceRoleKey);
        }
        if (method == HttpMethod.Post) request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Rag.Internal;

/// <summary>Single production transport: Pinecone REST API pinned to 2025-10, dense indexes only.</summary>
internal sealed class PineconeTransport(HttpClient client, PineconeServiceOptions options, ILogger<PineconeTransport> logger) : IPineconeTransport, IDisposable
{
    internal const string ApiVersion = "2025-10";
    private readonly ConcurrentDictionary<PineconeTargetOptions, string> _hosts = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);

    internal static bool IsSafeHost(string host) => Uri.TryCreate(host.Contains("://") ? host : "https://" + host, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Port == 443 && uri.Host.EndsWith(".pinecone.io", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);
    private static string NormalizeHost(string host) => new Uri(host.Contains("://") ? host : "https://" + host).GetLeftPart(UriPartial.Authority);

    public async Task EnsureReadyAsync(PineconeTargetOptions target, bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _hosts.ContainsKey(target)) return;
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _hosts.ContainsKey(target)) return;
            using var description = await SendAsync("https://api.pinecone.io/indexes/" + Uri.EscapeDataString(target.IndexName), HttpMethod.Get, null, cancellationToken);
            var root = description.RootElement;
            if (root.GetProperty("dimension").GetInt32() != OnnxEmbeddingModel.Dimension ||
                root.GetProperty("metric").GetString() != "cosine" ||
                (root.TryGetProperty("vector_type", out var type) && type.GetString() != "dense"))
                throw new PineconeServiceException(PineconeErrorCategory.IncompatibleIndex, "Target must be a 384-dimensional dense cosine index.");
            if (!root.GetProperty("status").GetProperty("ready").GetBoolean())
                throw Unavailable();
            var discovered = root.GetProperty("host").GetString();
            if (discovered is null || !IsSafeHost(discovered) ||
                (target.Host is not null && NormalizeHost(target.Host) != NormalizeHost(discovered)))
                throw new PineconeServiceException(PineconeErrorCategory.Configuration, "Configured target host does not match the described index.");
            var host = NormalizeHost(discovered);
            using var stats = await SendAsync(host + "/describe_index_stats", HttpMethod.Post, JsonSerializer.SerializeToUtf8Bytes(new { }), cancellationToken);
            if (stats.RootElement.GetProperty("dimension").GetInt32() != OnnxEmbeddingModel.Dimension)
                throw new PineconeServiceException(PineconeErrorCategory.IncompatibleIndex, "Data-plane dimension is incompatible.");
            _hosts[target] = host;
        }
        catch (OperationCanceledException) { _hosts.TryRemove(target, out _); throw; }
        catch (PineconeServiceException) { _hosts.TryRemove(target, out _); throw; }
        catch { _hosts.TryRemove(target, out _); throw Unavailable(); }
        finally { _initialization.Release(); }
    }

    public async Task UpsertAsync(PineconeTargetOptions target, IReadOnlyList<PineconeRecord> records, CancellationToken cancellationToken)
    {
        var payload = PineconePayload.Upsert(target, records);
        if (payload.Length > options.MaxPayloadBytes) throw PineconeMetadataMapper.Invalid("Upsert payload exceeds the configured limit.");
        logger.LogDebug("Pinecone upsert batch: {Count} records, {PayloadBytes} bytes", records.Count, payload.Length);
        using var response = await SendAsync(Host(target) + "/vectors/upsert", HttpMethod.Post, payload, cancellationToken);
        if (!response.RootElement.TryGetProperty("upsertedCount", out var count) || count.GetInt32() != records.Count)
            throw Unavailable();
    }

    public async Task<IReadOnlyList<PineconeMatch>> QueryAsync(PineconeTargetOptions target, float[] vector, int topK, JsonElement filter, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Host(target) + "/query", HttpMethod.Post, JsonSerializer.SerializeToUtf8Bytes(new
        { @namespace = target.Namespace, vector, topK, filter, includeMetadata = true, includeValues = false }), cancellationToken);
        try
        {
            return response.RootElement.GetProperty("matches").EnumerateArray().Select(m => new PineconeMatch(
                m.GetProperty("id").GetString()!, m.GetProperty("score").GetDouble(),
                m.TryGetProperty("metadata", out var metadata) ? metadata.Clone() : default)).ToArray();
        }
        catch { throw Unavailable(); }
    }

    public async Task DeleteAsync(PineconeTargetOptions target, JsonElement filter, CancellationToken cancellationToken)
    {
        // Filter deletion avoids fetching vectors and enforces scope/collection even for ID deletion.
        using var response = await SendAsync(Host(target) + "/vectors/delete", HttpMethod.Post,
            JsonSerializer.SerializeToUtf8Bytes(new { @namespace = target.Namespace, filter }), cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(string url, HttpMethod method, byte[]? payload, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);
            TimeSpan? retryAfter = null;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.Add("Api-Key", options.ApiKey);
                request.Headers.Add("X-Pinecone-API-Version", ApiVersion);
                if (payload is not null)
                {
                    request.Content = new ByteArrayContent(payload);
                    request.Content.Headers.ContentType = new("application/json");
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                    return JsonDocument.Parse(bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new PineconeServiceException(PineconeErrorCategory.Configuration, "Pinecone rejected dependency credentials or permissions.");
                if (response.StatusCode != HttpStatusCode.TooManyRequests && response.StatusCode != HttpStatusCode.RequestTimeout &&
                    response.StatusCode is not (HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                    throw Unavailable();
                retryAfter = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            catch (JsonException) { throw Unavailable(); }
            if (attempt >= options.MaxRetries) throw Unavailable();
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(10_000, options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt)) + Random.Shared.Next(100));
            if (delay > TimeSpan.FromMinutes(1)) throw Unavailable(); // do not retry earlier than Retry-After
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            PineconeDiagnostics.Retries.Add(1);
            logger.LogWarning("Pinecone transient failure; retry {Attempt} after {DelayMs}ms", attempt + 1, delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }
    }
    private string Host(PineconeTargetOptions target) => _hosts.TryGetValue(target, out var host) ? host : throw Unavailable();
    private static PineconeServiceException Unavailable() => new(PineconeErrorCategory.DependencyUnavailable, "Pinecone dependency request failed.");
    public void Dispose() { client.Dispose(); _initialization.Dispose(); }
}

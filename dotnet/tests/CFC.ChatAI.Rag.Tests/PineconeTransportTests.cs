using System.Net;
using System.Text.Json;
using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeTransportTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public Queue<Func<CancellationToken, Task<HttpResponseMessage>>> Responses { get; } = new();
        public List<(string Url, string? Body, string Version)> Requests { get; } = [];
        public void Add(string json, HttpStatusCode status = HttpStatusCode.OK) => Responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) }));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.GetValues("X-Pinecone-API-Version").Single()));
            return await Responses.Dequeue()(cancellationToken);
        }
    }
    private static void Ready(Handler handler, int dimension = 384, string metric = "cosine")
    {
        handler.Add(JsonSerializer.Serialize(new { dimension, metric, vector_type = "dense", host = "unit.svc.pinecone.io", status = new { ready = true } }));
        handler.Add("{\"dimension\":384,\"namespaces\":{}}");
    }

    [Fact]
    public async Task WireContractPinsApiAndRequestsMetadataButNeverQueryVectorValues()
    {
        var h = new Handler(); Ready(h);
        var f = new ServiceFixture();
        h.Add("{\"matches\":[]}"); h.Add("{\"upsertedCount\":1}"); h.Add("{}");
        using var t = new PineconeTransport(new HttpClient(h), f.Options, NullLogger<PineconeTransport>.Instance);
        await t.EnsureReadyAsync(f.Options.Documents, false, default);
        await t.EnsureReadyAsync(f.Options.Documents, false, default);
        await t.QueryAsync(f.Options.Documents, FakeEncoder.UnitVector(), 5, JsonSerializer.SerializeToElement(new { }), default);
        await t.UpsertAsync(f.Options.Documents, [new("id", FakeEncoder.UnitVector(), JsonSerializer.SerializeToElement(new { text = "full" }))], default);
        await t.DeleteAsync(f.Options.Documents, JsonSerializer.SerializeToElement(new { doc_id = "doc" }), default);
        Assert.Equal(5, h.Requests.Count); Assert.All(h.Requests, r => Assert.Equal("2025-10", r.Version));
        Assert.EndsWith("/describe_index_stats", h.Requests[1].Url);
        var query = JsonSerializer.Deserialize<JsonElement>(h.Requests[2].Body!);
        Assert.True(query.GetProperty("includeMetadata").GetBoolean()); Assert.False(query.GetProperty("includeValues").GetBoolean());
        Assert.Equal("unit-tests", query.GetProperty("namespace").GetString());
        var upsert = JsonSerializer.Deserialize<JsonElement>(h.Requests[3].Body!);
        Assert.Equal("full", upsert.GetProperty("vectors")[0].GetProperty("metadata").GetProperty("text").GetString());
        var delete = JsonSerializer.Deserialize<JsonElement>(h.Requests[4].Body!);
        Assert.True(delete.TryGetProperty("filter", out _)); Assert.False(delete.TryGetProperty("ids", out _));
    }

    [Theory]
    [InlineData(768, "cosine")]
    [InlineData(384, "dotproduct")]
    public async Task IncompatibleIndexIsNotRetriedOrEnabled(int dimension, string metric)
    {
        var h = new Handler(); Ready(h, dimension, metric); var f = new ServiceFixture();
        using var t = new PineconeTransport(new HttpClient(h), f.Options, NullLogger<PineconeTransport>.Instance);
        var error = await Assert.ThrowsAsync<PineconeServiceException>(() => t.EnsureReadyAsync(f.Options.Documents, false, default));
        Assert.Equal(PineconeErrorCategory.IncompatibleIndex, error.Category); Assert.Single(h.Requests);
    }

    [Fact]
    public async Task TransientErrorsRetryBoundedlyButAuthAndBadRequestsDoNot()
    {
        var f = new ServiceFixture(o => o with { MaxRetries = 2, RetryBaseDelay = TimeSpan.Zero });
        var h = new Handler(); h.Add("secret", HttpStatusCode.TooManyRequests); h.Add("secret", HttpStatusCode.ServiceUnavailable); Ready(h);
        using var t = new PineconeTransport(new HttpClient(h), f.Options, NullLogger<PineconeTransport>.Instance);
        await t.EnsureReadyAsync(f.Options.Documents, false, default); Assert.Equal(4, h.Requests.Count);
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest, HttpStatusCode.NotFound })
        {
            var failed = new Handler(); failed.Add("SECRET credentials", code);
            using var transport = new PineconeTransport(new HttpClient(failed), f.Options, NullLogger<PineconeTransport>.Instance);
            var error = await Assert.ThrowsAsync<PineconeServiceException>(() => transport.EnsureReadyAsync(f.Options.Documents, false, default));
            Assert.Single(failed.Requests); Assert.DoesNotContain("SECRET", error.Message); Assert.Null(error.InnerException);
        }
        var unavailable = new Handler(); for (int i = 0; i < 3; i++) unavailable.Add("secret", HttpStatusCode.ServiceUnavailable);
        using var exhausted = new PineconeTransport(new HttpClient(unavailable), f.Options, NullLogger<PineconeTransport>.Instance);
        await Assert.ThrowsAsync<PineconeServiceException>(() => exhausted.EnsureReadyAsync(f.Options.Documents, false, default));
        Assert.Equal(3, unavailable.Requests.Count);
    }

    [Fact]
    public async Task TimeoutsAreBoundedAndCallerCancellationIsNotRetried()
    {
        var f = new ServiceFixture(o => o with { RequestTimeout = TimeSpan.FromMilliseconds(10), MaxRetries = 0 });
        var h = new Handler();
        h.Responses.Enqueue(async ct => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        using var t = new PineconeTransport(new HttpClient(h), f.Options, NullLogger<PineconeTransport>.Instance);
        await Assert.ThrowsAsync<PineconeServiceException>(() => t.EnsureReadyAsync(f.Options.Documents, false, default));
        Assert.Single(h.Requests);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.EnsureReadyAsync(f.Options.Documents, false, cts.Token));
        Assert.Single(h.Requests);
    }
}

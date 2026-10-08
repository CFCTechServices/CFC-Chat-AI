using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeServiceRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateAndSharedTargetsKeepCollectionsIsolated(bool shared)
    {
        var f = new ServiceFixture(o => o with { Videos = shared ? o.Documents : o.Videos });
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk("same")]);
        await f.Service.UpsertChunksAsync(ChunkCollection.Videos, [ServiceFixture.Chunk("same", ChunkCollection.Videos)]);
        Assert.Equal(f.Options.Documents, f.Transport.Writes[0].Target);
        Assert.Equal(f.Options.Videos, f.Transport.Writes[1].Target);
        Assert.NotEqual(f.Transport.Writes[0].Records[0].Id, f.Transport.Writes[1].Records[0].Id);
        f.Transport.Matches = [f.Match(ServiceFixture.Chunk("doc")),
            f.Match(ServiceFixture.Chunk("video", ChunkCollection.Videos), collection: ChunkCollection.Videos)];
        var results = await f.Service.SearchAsync(ChunkCollection.Videos, "question", new());
        Assert.Equal("video", Assert.Single(results).Chunk.ChunkId);
        Assert.Equal(f.Options.Videos, f.Transport.QueryTarget);
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.SearchAsync(ChunkCollection.Videos, "q", new() { SourceType = ChunkSourceType.Document }));
    }

    [Fact]
    public async Task HealthChecksDataTargetsDeduplicatesSharedTargetsAndSeparatesModelReadiness()
    {
        var f = new ServiceFixture(o => o with { Videos = o.Documents });
        f.Encoder.Ready = false;
        var health = await f.Service.GetHealthAsync();
        Assert.False(health.ModelReady); Assert.False(health.Ready);
        Assert.All(health.Targets, t => Assert.True(t.Ready));
        Assert.True(Assert.Single(f.Transport.Checks).Refresh);
        Assert.InRange(health.CheckedAt, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
        f.Transport.ReadinessFailure = new Exception("SECRET_API_KEY raw sdk detail");
        health = await f.Service.GetHealthAsync();
        Assert.All(health.Targets, t => { Assert.False(t.Ready); Assert.DoesNotContain("SECRET", t.Diagnostic); });
    }

    [Fact]
    public void ConfigurationRequiresExplicitModelCompatibilityAndSafeHosts()
    {
        Assert.Throws<PineconeServiceException>(() => new ServiceFixture(o => o with { Documents = o.Documents with { EmbeddingModelIdentity = "different-model" } }));
        Assert.Throws<PineconeServiceException>(() => new ServiceFixture(o => o with { Documents = o.Documents with { Host = "https://evil.example" } }));
        Assert.Throws<PineconeServiceException>(() => new ServiceFixture(o => o with { ApiKey = "your-pinecone-api-key" }));
        Assert.Throws<PineconeServiceException>(() => new ServiceFixture(o => o with { BatchSize = 1001 }));
        Assert.True(PineconeTransport.IsSafeHost("example.svc.us-east-1.pinecone.io"));
        Assert.False(PineconeTransport.IsSafeHost("https://example.pinecone.io/path"));
        Assert.False(PineconeTransport.IsSafeHost("http://example.pinecone.io"));
    }

    [Fact]
    public async Task TrustedScopeIsSnapshottedAndRejectsUnauthorizedWrites()
    {
        var ids = new List<string> { "allowed" };
        var f = new ServiceFixture(o => o with { AllowedDocumentIds = ids });
        ids.Add("outside");
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk(document: "outside")]));
        Assert.Empty(f.Transport.Writes);
    }
}

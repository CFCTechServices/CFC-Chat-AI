using System.Text.Json;
using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeServiceSearchTests
{
    [Fact]
    public async Task ReconstructsCompleteChunksPreservesOrderingAndAssignsFinalRanks()
    {
        var f = new ServiceFixture();
        f.Transport.Matches = [f.Match(ServiceFixture.Chunk("second"), 0.8), f.Match(ServiceFixture.Chunk("first"), 0.9)];
        var results = await f.Service.SearchAsync(ChunkCollection.Documents, "feed question", new() { TopK = 2 });
        Assert.Equal(new[] { "second", "first" }, results.Select(r => r.Chunk.ChunkId));
        Assert.Equal(new[] { 1, 2 }, results.Select(r => r.Rank));
        Assert.Equal(0.8, results[0].SimilarityScore); Assert.Null(results[0].RerankScore);
        Assert.Equal(ServiceFixture.Chunk().Text, results[0].Chunk.Text);
        Assert.Equal("feed question", Assert.Single(f.Encoder.Texts));
        Assert.Same(f.Encoder.Vector, f.Transport.QueryVector);
        Assert.Equal(2, f.Transport.QueryTopK);
        Assert.Single(f.Transport.Checks);
    }

    [Fact]
    public async Task InvalidMetadataLowSimilarityAndDuplicatesAreSkippedWithoutHydration()
    {
        var f = new ServiceFixture();
        f.Transport.Matches = [new("legacy", 0.99, JsonSerializer.SerializeToElement(new { doc_id = "old" })),
            f.Match(ServiceFixture.Chunk("low"), 0.1), f.Match(ServiceFixture.Chunk("valid"), 0.8), f.Match(ServiceFixture.Chunk("valid"), 0.8)];
        var results = await f.Service.SearchAsync(ChunkCollection.Documents, "question", new() { MinimumSimilarity = 0.5 });
        var result = Assert.Single(results); Assert.Equal("valid", result.Chunk.ChunkId); Assert.Equal(1, result.Rank);
    }

    [Fact]
    public async Task ScopeAndTypedFiltersAreAndedAndRecheckedOnReturnedRecords()
    {
        var f = new ServiceFixture(o => o with { AllowedDocumentIds = ["allowed", "also-allowed"] });
        f.Transport.Matches = [f.Match(ServiceFixture.Chunk("unauthorized", document: "outside") with { SectionId = "s" }),
            f.Match(ServiceFixture.Chunk("wrong-filter", document: "also-allowed") with { SectionId = "s" }),
            f.Match(ServiceFixture.Chunk("wrong-section", document: "allowed") with { SectionId = "other" }),
            f.Match(ServiceFixture.Chunk("correct", document: "allowed") with { SectionId = "s" })];
        var result = await f.Service.SearchAsync(ChunkCollection.Documents, "question", new() { DocumentIds = ["allowed"], SectionId = "s" });
        Assert.Equal("correct", Assert.Single(result).Chunk.ChunkId);
        var and = f.Transport.QueryFilter.GetProperty("$and").EnumerateArray().ToArray();
        Assert.Equal(2, and.Count(c => c.TryGetProperty("doc_id", out _)));
        Assert.Contains(and, c => c.TryGetProperty("section_id", out var v) && v.GetProperty("$eq").GetString() == "s");
        Assert.Contains(and, c => c.TryGetProperty("source_type", out var v) && v.GetProperty("$eq").GetString() == "document");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidQueryLimitsAreRejectedBeforeNetwork(int topK)
    {
        var f = new ServiceFixture();
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.SearchAsync(ChunkCollection.Documents, "query", new() { TopK = topK }));
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.SearchAsync(ChunkCollection.Documents, " ", new()));
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.SearchAsync(ChunkCollection.Documents, "q", new() { MinimumSimilarity = double.NaN }));
        Assert.Empty(f.Transport.Checks);
    }

    [Fact]
    public async Task DependencyOutageIsNotAnEmptySearch()
    {
        var f = new ServiceFixture(); f.Transport.QueryFailure = FakeTransport.Failure();
        var error = await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.SearchAsync(ChunkCollection.Documents, "question", new()));
        Assert.Equal(PineconeErrorCategory.DependencyUnavailable, error.Category);
    }

    [Fact]
    public async Task EmptyScopeOrDocumentFilterDoesNotWidenSearch()
    {
        var f = new ServiceFixture(o => o with { AllowedDocumentIds = [] });
        Assert.Empty(await f.Service.SearchAsync(ChunkCollection.Documents, "question", new()));
        Assert.Empty(f.Transport.Checks);
        var unrestricted = new ServiceFixture();
        Assert.Empty(await unrestricted.Service.SearchAsync(ChunkCollection.Documents, "question", new() { DocumentIds = [] }));
        Assert.Empty(unrestricted.Transport.Checks);
    }
}

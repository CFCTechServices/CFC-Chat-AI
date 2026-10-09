using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeServiceDeleteTests
{
    [Fact]
    public async Task IdDeletesAreBatchedScopedAndIdempotentWithUnknownAffectedCounts()
    {
        var f = new ServiceFixture(o => o with { AllowedDocumentIds = ["authorized-doc"] });
        var result = await f.Service.DeleteChunksAsync(ChunkCollection.Videos, ["c1", "c2", "c3"]);
        Assert.Equal(ChunkOperationStatus.Acknowledged, result.Status);
        Assert.Equal(new[] { "c1", "c2", "c3" }, result.AcknowledgedIds); Assert.Null(result.AffectedCount);
        Assert.Equal(2, f.Transport.Deletes.Count);
        var filter = f.Transport.Deletes[0].Filter.GetRawText();
        Assert.Contains("\"chunk_id\"", filter); Assert.Contains("authorized-doc", filter); Assert.Contains("video", filter);
        Assert.Equal(f.Options.Videos, f.Transport.Deletes[0].Target);
        await f.Service.DeleteChunksAsync(ChunkCollection.Videos, ["c1", "c2", "c3"]);
        Assert.Equal(4, f.Transport.Deletes.Count);
        Assert.Empty(f.Encoder.Texts);
    }

    [Fact]
    public async Task PartialDeletesIdentifyUnresolvedIdsAndAreIndeterminate()
    {
        var f = new ServiceFixture(); f.Transport.FailDeleteNumber = 2;
        var error = await Assert.ThrowsAsync<ChunkOperationException>(() => f.Service.DeleteChunksAsync(ChunkCollection.Documents, ["1", "2", "3"]));
        Assert.Equal(new[] { "1", "2" }, error.CompletedIds); Assert.Equal(new[] { "3" }, error.UnresolvedIds);
        Assert.True(error.IsIndeterminate); Assert.Equal(PineconeErrorCategory.PartialDelete, error.Category);
    }

    [Fact]
    public async Task EmptyDeletesNoOpAndInvalidIdsRejectedBeforeSideEffects()
    {
        var f = new ServiceFixture();
        Assert.Equal(ChunkOperationStatus.NoOp, (await f.Service.DeleteChunksAsync(ChunkCollection.Documents, [])).Status);
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.DeleteChunksAsync(ChunkCollection.Documents, ["a", "a"]));
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.DeleteChunksAsync(ChunkCollection.Documents, [" "]));
        Assert.Empty(f.Transport.Checks); Assert.Empty(f.Transport.Deletes);
    }

    [Fact]
    public async Task CallerDrivenReplacementDeletesOnlyExplicitObsoleteIds()
    {
        var f = new ServiceFixture();
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk("retained"), ServiceFixture.Chunk("old")]);
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk("retained"), ServiceFixture.Chunk("new")]);
        Assert.Empty(f.Transport.Deletes);
        await f.Service.DeleteChunksAsync(ChunkCollection.Documents, ["old"]);
        var filter = Assert.Single(f.Transport.Deletes).Filter.GetRawText();
        Assert.Contains("old", filter); Assert.DoesNotContain("new", filter); Assert.DoesNotContain("retained", filter);
    }
}

using CFC.ChatAI.Rag.Contracts;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeServiceWriteTests
{
    [Fact]
    public async Task EmptyWritesAreNoOpWithoutEmbeddingOrNetwork()
    {
        var f = new ServiceFixture();
        var result = await f.Service.UpsertChunksAsync(ChunkCollection.Documents, []);
        Assert.Equal(ChunkOperationStatus.NoOp, result.Status);
        Assert.Empty(f.Encoder.Texts); Assert.Empty(f.Transport.Checks);
    }

    [Fact]
    public async Task ValidationAndOversizeFailuresHappenBeforeAnyWrites()
    {
        var f = new ServiceFixture();
        foreach (var invalid in new[]
        {
            ServiceFixture.Chunk() with { Text = " " }, ServiceFixture.Chunk() with { DocumentId = "" },
            ServiceFixture.Chunk() with { SourceType = ChunkSourceType.Video },
            ServiceFixture.Chunk() with { StartSeconds = double.NaN }, ServiceFixture.Chunk() with { StartSeconds = -1 },
            ServiceFixture.Chunk() with { StartSeconds = 3, EndSeconds = 0 },
            ServiceFixture.Chunk() with { Text = new string('x', 40_001) }
        })
            await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk("valid"), invalid]));
        await Assert.ThrowsAsync<PineconeServiceException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk(), ServiceFixture.Chunk()]));
        Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Checks); Assert.Empty(f.Encoder.Texts);
    }

    [Fact]
    public async Task WritesFullMetadataAndStableVectorsInBoundedBatches()
    {
        var f = new ServiceFixture();
        var chunks = Enumerable.Range(0, 5).Select(i => ServiceFixture.Chunk("c" + i)).ToArray();
        var result = await f.Service.UpsertChunksAsync(ChunkCollection.Documents, chunks);
        Assert.Equal(chunks.Select(c => c.ChunkId), result.AcknowledgedIds);
        Assert.Equal(new[] { 2, 2, 1 }, f.Transport.Writes.Select(w => w.Records.Length));
        Assert.Equal(chunks.Select(c => c.Text), f.Encoder.Texts);
        Assert.All(f.Transport.Writes.SelectMany(w => w.Records), r =>
        {
            Assert.Equal(384, r.Values.Length);
            Assert.Equal(chunks[0].Text, r.Metadata.GetProperty("text").GetString());
        });
        var originalIds = f.Transport.Writes.SelectMany(w => w.Records).Select(r => r.Id).ToArray();
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, chunks);
        Assert.Equal(originalIds, f.Transport.Writes.Skip(3).SelectMany(w => w.Records).Select(r => r.Id));
    }

    [Fact]
    public async Task PayloadAwareSplittingDoesNotDropContent()
    {
        var f = new ServiceFixture(o => o with { BatchSize = 10, MaxPayloadBytes = 11_000 });
        var chunks = Enumerable.Range(0, 4).Select(i => ServiceFixture.Chunk("c" + i) with { Text = new string('x', 2500) }).ToArray();
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, chunks);
        Assert.True(f.Transport.Writes.Count > 1);
        Assert.Equal(4, f.Transport.Writes.Sum(w => w.Records.Length));
        Assert.All(f.Transport.Writes, w => Assert.True(Internal.PineconePayload.Upsert(w.Target, w.Records).Length <= 11_000));
    }

    [Fact]
    public async Task PartialWritesReportCompletedAndUnresolvedIncludingTimedOutBatch()
    {
        var f = new ServiceFixture(); f.Transport.FailWriteNumber = 2;
        var chunks = Enumerable.Range(0, 5).Select(i => ServiceFixture.Chunk("c" + i)).ToArray();
        var error = await Assert.ThrowsAsync<ChunkOperationException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, chunks));
        Assert.Equal(PineconeErrorCategory.PartialWrite, error.Category);
        Assert.Equal(new[] { "c0", "c1" }, error.CompletedIds);
        Assert.Equal(new[] { "c2", "c3", "c4" }, error.UnresolvedIds);
        Assert.True(error.IsIndeterminate); Assert.False(error.WasCanceled);
        Assert.NotEmpty(error.OperationId); Assert.Null(error.InnerException);
        f.Transport.FailWriteNumber = null;
        await f.Service.UpsertChunksAsync(ChunkCollection.Documents, chunks.Where(c => error.UnresolvedIds.Contains(c.ChunkId)).ToArray());
        Assert.Equal(new[] { "c2", "c3", "c4" }, f.Transport.Writes.Skip(2).SelectMany(w => w.Records).Select(r => r.Metadata.GetProperty("chunk_id").GetString()));
    }

    [Theory]
    [InlineData("dimension")]
    [InlineData("nan")]
    [InlineData("zero")]
    [InlineData("norm")]
    public async Task InvalidModelOutputsCannotBeSent(string invalid)
    {
        var f = new ServiceFixture();
        f.Encoder.Vector = invalid switch
        {
            "dimension" => [1], "nan" => Enumerable.Repeat(float.NaN, 384).ToArray(),
            "zero" => new float[384], _ => Enumerable.Repeat(1f, 384).ToArray()
        };
        var error = await Assert.ThrowsAsync<ChunkOperationException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk()]));
        Assert.False(error.IsIndeterminate); Assert.Empty(f.Transport.Writes);
        Assert.Equal(PineconeErrorCategory.IncompatibleIndex, error.FailureCategory);
    }

    [Fact]
    public async Task CancellationBeforeDispatchIsCancellationAndAfterDispatchIsIndeterminate()
    {
        var f = new ServiceFixture();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk()], cts.Token));
        Assert.Empty(f.Transport.Writes);
        f.Transport.CancelWrite = true;
        var error = await Assert.ThrowsAsync<ChunkOperationException>(() => f.Service.UpsertChunksAsync(ChunkCollection.Documents, [ServiceFixture.Chunk()]));
        Assert.True(error.IsIndeterminate); Assert.True(error.WasCanceled); Assert.Single(error.UnresolvedIds);
    }
}

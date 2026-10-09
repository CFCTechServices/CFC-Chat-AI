using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeIntegrationFactAttribute : FactAttribute
{
    public PineconeIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_PINECONE_INTEGRATION") != "1")
            Skip = "Opt-in: requires a dedicated disposable test index, separate PINECONE_TEST_* credentials, and ONNX assets.";
    }
}

public sealed class PineconeIntegrationTests
{
    [PineconeIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task RealMetadataRoundTripSharedCollectionIsolationAndChunkDeletion()
    {
        // Never read production PINECONE_API_KEY/PINECONE_INDEX_NAME. Namespace is unique per run.
        var identity = OnnxEmbeddingCompatibilityTests.Required("PINECONE_TEST_MODEL_IDENTITY");
        var target = new PineconeTargetOptions
        {
            IndexName = OnnxEmbeddingCompatibilityTests.Required("PINECONE_TEST_INDEX_NAME"),
            Namespace = "cfc-service-test-" + Guid.NewGuid().ToString("N"), EmbeddingModelIdentity = identity
        };
        var embedding = new EmbeddingOptions
        {
            ModelPath = OnnxEmbeddingCompatibilityTests.Required("ONNX_TEST_MODEL_PATH"),
            VocabularyPath = OnnxEmbeddingCompatibilityTests.Required("ONNX_TEST_VOCAB_PATH"), ModelIdentity = identity
        };
        var options = new PineconeServiceOptions
        {
            ApiKey = OnnxEmbeddingCompatibilityTests.Required("PINECONE_TEST_API_KEY"), Documents = target, Videos = target,
            AllowedDocumentIds = ["test-doc", "test-video"], RequestTimeout = TimeSpan.FromSeconds(15), MaxRetries = 1
        };
        await using var provider = new ServiceCollection().AddPineconeService(options, embedding).BuildServiceProvider();
        var service = provider.GetRequiredService<IPineconeService>();
        var document = ServiceFixture.Chunk("shared-id", document: "test-doc") with { SectionId = "s1", ImagePaths = ["images/牛.png"] };
        var video = ServiceFixture.Chunk("shared-id", ChunkCollection.Videos, "test-video") with
        { StartSeconds = 0, EndSeconds = 5.25, VideoUrl = "https://example.org/reference-only" };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            Assert.True((await service.GetHealthAsync(deadline.Token)).Ready);
            var write = await service.UpsertChunksAsync(ChunkCollection.Documents, [document], deadline.Token);
            Assert.Equal(new[] { "shared-id" }, write.AcknowledgedIds);
            await service.UpsertChunksAsync(ChunkCollection.Videos, [video], deadline.Token);
            var docs = await Poll(service, ChunkCollection.Documents, document.Text, 1, deadline.Token);
            var videos = await Poll(service, ChunkCollection.Videos, video.Text, 1, deadline.Token);
            Assert.Equal(document.Text, docs[0].Chunk.Text); Assert.Equal(document.ImagePaths, docs[0].Chunk.ImagePaths);
            Assert.Equal("test-doc", docs[0].Chunk.DocumentId); Assert.Equal("test-video", videos[0].Chunk.DocumentId);
            Assert.Equal(0, videos[0].Chunk.StartSeconds); Assert.Equal(video.VideoUrl, videos[0].Chunk.VideoUrl);
            var oversized = document with { ChunkId = "oversized", Text = new string('牛', 40_001) };
            var error = await Assert.ThrowsAsync<PineconeServiceException>(() => service.UpsertChunksAsync(ChunkCollection.Documents, [oversized], deadline.Token));
            Assert.Equal(PineconeErrorCategory.OversizedChunk, error.Category);
            var deleted = await service.DeleteChunksAsync(ChunkCollection.Documents, [document.ChunkId], deadline.Token);
            Assert.Equal(ChunkOperationStatus.Acknowledged, deleted.Status);
            Assert.Equal(new[] { document.ChunkId }, deleted.AcknowledgedIds); Assert.Null(deleted.AffectedCount);
            await Poll(service, ChunkCollection.Documents, document.Text, 0, deadline.Token);
            Assert.Single(await service.SearchAsync(ChunkCollection.Videos, video.Text, new(), deadline.Token));
            var idDelete = await service.DeleteChunksAsync(ChunkCollection.Videos, [video.ChunkId], deadline.Token);
            Assert.Single(idDelete.AcknowledgedIds); Assert.Null(idDelete.AffectedCount);
            await service.DeleteChunksAsync(ChunkCollection.Videos, [video.ChunkId], deadline.Token);
            await Poll(service, ChunkCollection.Videos, video.Text, 0, deadline.Token);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await service.DeleteChunksAsync(ChunkCollection.Documents, [document.ChunkId], cleanup.Token);
            await service.DeleteChunksAsync(ChunkCollection.Videos, [video.ChunkId], cleanup.Token);
        }
    }

    private static async Task<IReadOnlyList<ChunkSearchMatch>> Poll(IPineconeService service, ChunkCollection collection, string query, int expected, CancellationToken cancellationToken)
    {
        while (true)
        {
            var results = await service.SearchAsync(collection, query, new(), cancellationToken);
            if (results.Count == expected) return results;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

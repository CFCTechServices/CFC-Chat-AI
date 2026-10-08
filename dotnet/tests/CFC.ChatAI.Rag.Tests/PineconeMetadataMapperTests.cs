using System.Text;
using System.Text.Json;
using CFC.ChatAI.Rag.Contracts;
using CFC.ChatAI.Rag.Internal;

namespace CFC.ChatAI.Rag.Tests;

public sealed class PineconeMetadataMapperTests
{
    [Fact]
    public void FullContentRoundTripPreservesUnicodeListsAndZeroTimestamps()
    {
        var chunk = ServiceFixture.Chunk(collection: ChunkCollection.Videos) with
        {
            Text = new string('牛', 5000), SectionId = "s1", SectionTitle = "Café", SectionPath = "Guide/Feed",
            ImagePaths = Array.AsReadOnly(new[] { "images/牛.png", "https://example.org/image" }),
            BlockIds = ["block-1", "block-2"], StartSeconds = 0, EndSeconds = 12.25,
            ArtifactUrl = "storage/video.mp4", VideoUrl = "https://example.org/video"
        };
        var metadata = PineconeMetadataMapper.Encode(ChunkCollection.Videos, chunk, "v1", 40_000);
        var result = PineconeMetadataMapper.Decode(ChunkCollection.Videos,
            PineconeMetadataMapper.RecordId(ChunkCollection.Videos, chunk.ChunkId), metadata, "v1");
        Assert.NotNull(result);
        Assert.Equal(chunk.Text, result.Text); Assert.Equal(chunk.ChunkId, result.ChunkId);
        Assert.Equal(chunk.DocumentId, result.DocumentId); Assert.Equal(chunk.Source, result.Source);
        Assert.Equal(chunk.SourceType, result.SourceType); Assert.Equal(chunk.SectionId, result.SectionId);
        Assert.Equal(chunk.SectionTitle, result.SectionTitle); Assert.Equal(chunk.SectionPath, result.SectionPath);
        Assert.Equal(chunk.ImagePaths, result.ImagePaths); Assert.Equal(chunk.BlockIds, result.BlockIds);
        Assert.Equal(0, result.StartSeconds); Assert.Equal(12.25, result.EndSeconds);
        Assert.Equal(chunk.ArtifactUrl, result.ArtifactUrl); Assert.Equal(chunk.VideoUrl, result.VideoUrl);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(result.ImagePaths);
    }

    [Fact]
    public void RepositoryRealContentFitsWithoutHiddenTruncationOrSplitting()
    {
        // A full real guide is larger than the current upstream 1200-char/500-word chunks.
        // This is a feasibility sample, not a guarantee that every future chunk will fit.
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "representative_document.md"));
        var chunk = ServiceFixture.Chunk() with
        {
            Text = text, Source = "CFC Beef Model Guide.DOC", SectionTitle = "CFC Beef Feeder Growout Model",
            SectionPath = "Beef/Feeder Growout", ImagePaths = ["documents/beef/figures/figure-1.png"], BlockIds = ["guide-body"]
        };
        var metadata = PineconeMetadataMapper.Encode(ChunkCollection.Documents, chunk, "v1", 40_000);
        Assert.True(Encoding.UTF8.GetByteCount(metadata.GetRawText()) < 40_000);
        var result = PineconeMetadataMapper.Decode(ChunkCollection.Documents,
            PineconeMetadataMapper.RecordId(ChunkCollection.Documents, chunk.ChunkId), metadata, "v1")!;
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void MissingOptionalsAreOmittedRatherThanNullOrZero()
    {
        var metadata = PineconeMetadataMapper.Encode(ChunkCollection.Documents, ServiceFixture.Chunk(), "v1", 40_000);
        Assert.False(metadata.TryGetProperty("start_seconds", out _));
        Assert.False(metadata.TryGetProperty("section_id", out _));
        Assert.False(metadata.TryGetProperty("image_paths", out _));
        Assert.All(metadata.EnumerateObject(), field => Assert.Contains(field.Value.ValueKind,
            new[] { JsonValueKind.String, JsonValueKind.Number, JsonValueKind.Array }));
    }

    [Fact]
    public void SizeUsesSerializedUtf8IncludingEscapingAndFieldNames()
    {
        var chunk = ServiceFixture.Chunk() with { Text = "🐄 Café \"quoted\"\n" };
        var metadata = PineconeMetadataMapper.Encode(ChunkCollection.Documents, chunk, "v1", 40_000);
        int size = Encoding.UTF8.GetByteCount(metadata.GetRawText());
        PineconeMetadataMapper.Encode(ChunkCollection.Documents, chunk, "v1", size);
        var error = Assert.Throws<PineconeServiceException>(() => PineconeMetadataMapper.Encode(ChunkCollection.Documents, chunk, "v1", size - 1));
        Assert.Equal(PineconeErrorCategory.OversizedChunk, error.Category);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schema_version\":99}")]
    [InlineData("{\"schema_version\":null}")]
    [InlineData("[]")]
    public void InvalidOrLegacyMetadataDoesNotProducePseudoChunks(string json)
    {
        Assert.Null(PineconeMetadataMapper.Decode(ChunkCollection.Documents, "id", JsonSerializer.Deserialize<JsonElement>(json), "v1"));
    }

    [Fact]
    public void IdentityAndRecordIdAreVerifiedAndUnknownFieldsAreNotHydrated()
    {
        var chunk = ServiceFixture.Chunk();
        var metadata = PineconeMetadataMapper.Encode(ChunkCollection.Documents, chunk, "v1", 40_000);
        Assert.Null(PineconeMetadataMapper.Decode(ChunkCollection.Documents, "wrong-id", metadata, "v1"));
        Assert.Null(PineconeMetadataMapper.Decode(ChunkCollection.Documents, PineconeMetadataMapper.RecordId(ChunkCollection.Documents, chunk.ChunkId), metadata, "v2"));
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadata.GetRawText())!;
        dict["external_database_id"] = JsonSerializer.SerializeToElement("ignored");
        var reconstructed = PineconeMetadataMapper.Decode(ChunkCollection.Documents, PineconeMetadataMapper.RecordId(ChunkCollection.Documents, chunk.ChunkId), JsonSerializer.SerializeToElement(dict), "v1")!;
        var remapped = PineconeMetadataMapper.Encode(ChunkCollection.Documents, reconstructed, "v1", 40_000);
        Assert.False(remapped.TryGetProperty("external_database_id", out _));
    }

    [Fact]
    public void RecordIdsAreDeterministicBoundedAndCollectionSafe()
    {
        var id = new string('牛', 10_000);
        Assert.Equal(PineconeMetadataMapper.RecordId(ChunkCollection.Documents, id), PineconeMetadataMapper.RecordId(ChunkCollection.Documents, id));
        Assert.NotEqual(PineconeMetadataMapper.RecordId(ChunkCollection.Documents, id), PineconeMetadataMapper.RecordId(ChunkCollection.Videos, id));
        Assert.True(PineconeMetadataMapper.RecordId(ChunkCollection.Documents, id).Length < 512);
    }
}

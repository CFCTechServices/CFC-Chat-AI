using CFC.ChatAI.Pinecone.Internal;
namespace CFC.ChatAI.Pinecone.Tests;

/// <summary>
/// FormatContext must produce exactly what Python's format_context() produces.
/// Each chunk below is 37 characters: "[CHUNK_ID: a]\n" (14) + "Source: guide.docx\n" (19) + "One\n" (4).
/// </summary>
public class RagPipelineFormatTests
{
    private const string ChunkA = "[CHUNK_ID: a]\nSource: guide.docx\nOne\n";
    private const string ChunkC = "[CHUNK_ID: c]\nSource: guide.docx\nTwo\n";

    // Small helper so each test doesn't repeat every property
    private static ContextChunk Chunk(int rank, string? id, string text, string? title = null) =>
        new() { Rank = rank, ChunkId = id, Text = text, Source = "guide.docx", SectionTitle = title };
    
    [Fact]
    public void ChunkWithTitle_IncludesTitleLine()
    {
        var result = RagPipeline.FormatContext([Chunk(1, "abc", "Body text.", title: "Adding Ingredients")]);

        Assert.Equal("[CHUNK_ID: abc]\nSource: guide.docx\nTitle: Adding Ingredients\nBody text.\n", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyTitle_hasNoTitleLine(string? title)
    {
        var result = RagPipeline.FormatContext([Chunk(1, "a", "One", title)]);

        Assert.Equal(ChunkA, result);
    }

    [Fact]
    public void MultipleChunks_AreJoinedWithSeparator()
    {
        var result = RagPipeline.FormatContext([Chunk(1, "a", "One"), Chunk(2, "c", "Two")]);

        Assert.Equal(ChunkA + "\n---\n" + ChunkC, result);
    }

    [Fact]
    public void SeparatorIsNotCountedTowardMaxLength()
    {
        // 37 + 37 = 74 fits exactly. If the 5-char separator counted, chunk c would be dropped.
        var result = RagPipeline.FormatContext([Chunk(1, "a", "One"), Chunk(2, "c", "Two")], maxLength: 74);

        Assert.Equal(ChunkA + "\n---\n" + ChunkC, result);
    }

    [Fact]
    public void StopsAtFirstChunkThatDoesNotFit_EvenIfALaterOneWould()
    {
        var tooLong = Chunk(2, "b", new string('x', 100));

        var result = RagPipeline.FormatContext([Chunk(1, "a", "One"), tooLong, Chunk(3, "c", "Two")], maxLength: 74);

        Assert.Equal(ChunkA, result);   // c would fit (37 + 37 = 74) but Python breaks, not skips
    }

    [Fact]
    public void MissingChunkId_ShowsUnknown()
    {
        var result = RagPipeline.FormatContext([Chunk(1, null, "One")]);

        Assert.StartsWith("[CHUNK_ID: unknown]\n", result);
    }

    [Fact]
    public void EmptyList_ReturnsEmptyString()
    {
        Assert.Equal("", RagPipeline.FormatContext([]));
    }
}
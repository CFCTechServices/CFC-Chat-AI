namespace CFC.ChatAI.DocumentProcessor.Tests;

// Processor-only smoke checks. They do not exercise an endpoint or capture provider writes.
public sealed class UploadParityFixtureTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "UploadParityFixtures", name);

    [Fact]
    public async Task MultiSectionDocxHasTwoSearchableSections()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());
        await using var content = File.OpenRead(Fixture("multi-section.docx"));
        var result = await processor.ProcessAsync(new DocumentInput("multi-section.docx", content));

        Assert.Equal(new[] { "Overview", "Details" }, result.Sections.Select(s => s.Title));
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(result.Sections.Select(s => s.Id), result.Chunks.Select(c => c.SectionId));
    }

    [Fact]
    public async Task EmbeddedImageDocxHasSearchableTextAndImage()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());
        await using var content = File.OpenRead(Fixture("embedded-image.docx"));
        var result = await processor.ProcessAsync(new DocumentInput("embedded-image.docx", content));

        Assert.Single(result.Sections);
        Assert.Single(result.Chunks);
        Assert.Contains("Synthetic text before the image", result.Chunks[0].Text);
        Assert.Contains("Synthetic text after the image", result.Chunks[0].Text);
        var image = Assert.Single(result.Images);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(".png", image.Extension);
        Assert.Equal("f688dd6e21f010dd1052e979d9bd19c3a054ca0266e15e5e5673f97e3f7f9271", image.Id);
        Assert.Equal(68, image.Content.Length);
        Assert.Equal(new[] { image.Id }, result.Chunks[0].ImageIds);
    }

    [Theory]
    [InlineData("baseline.eml", "baseline")]
    [InlineData("unsupported.txt", "unsupported")]
    public async Task SyntheticFixtureProducesSearchableContent(string fileName, string documentId)
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());
        await using var content = File.OpenRead(Fixture(fileName));
        var result = await processor.ProcessAsync(new DocumentInput(fileName, content));

        Assert.Equal(documentId, result.DocumentId);
        Assert.Single(result.Sections);
        Assert.Single(result.Chunks);
        Assert.Contains("synthetic", result.Chunks[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Images);
        Assert.Empty(result.Chunks[0].ImageIds);
    }
}

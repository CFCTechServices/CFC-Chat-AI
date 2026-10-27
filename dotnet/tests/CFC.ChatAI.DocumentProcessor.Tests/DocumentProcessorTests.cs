using System.IO.Compression;
using System.Text;
using CFC.ChatAI.DocumentProcessor;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.DocumentProcessor.Tests;

public sealed class DocumentProcessorTests
{
    [Fact]
    public async Task ProcessesTextAndSplitsItAtConfiguredMaximum()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions { MaxChunkCharacters = 32 });
        var text = "A first sentence with enough words. A second sentence that is also long enough. Final words.";

        var result = await processor.ProcessAsync(Input("My Guide.txt", text));

        Assert.Equal("my-guide", result.DocumentId);
        Assert.Equal("My Guide.txt", result.SourceName);
        Assert.Equal("document", result.SourceType);
        Assert.Single(result.Sections);
        Assert.True(result.Chunks.Count > 1);
        Assert.All(result.Chunks, chunk => Assert.InRange(chunk.Text.Length, 1, 32));
        Assert.Equal(text, string.Join(' ', result.Chunks.Select(chunk => chunk.Text)));
    }

    [Fact]
    public async Task UsesMarkdownHeadingsAsSections()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());

        var result = await processor.ProcessAsync(Input("guide.md", "Preamble.\n\n# Getting Started\nInstall it.\n## Next Steps\nUse it."));

        Assert.Equal(new[] { "Document", "Getting Started", "Next Steps" }, result.Sections.Select(section => section.Title));
        Assert.Equal(new[] { 1, 1, 2 }, result.Sections.Select(section => section.Level));
        Assert.Equal(new[] { "Preamble.", "Install it.", "Use it." }, result.Chunks.Select(chunk => chunk.Text));
    }

    [Fact]
    public async Task ExtractsDocxHeadingsParagraphsAndTablesInBodyOrder()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());
        var docx = CreateDocx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Overview</w:t></w:r></w:p>
              <w:p><w:r><w:t>Welcome to the guide.</w:t></w:r></w:p>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Code</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Meaning</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
              <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Details</w:t></w:r></w:p>
              <w:p><w:r><w:t>More information.</w:t></w:r></w:p>
              <w:sectPr/>
            </w:body></w:document>
            """);

        var result = await processor.ProcessAsync(new DocumentInput("manual.docx", new MemoryStream(docx)));

        Assert.Equal(new[] { "Overview", "Details" }, result.Sections.Select(section => section.Title));
        Assert.Equal(new[] { 1, 2 }, result.Sections.Select(section => section.Level));
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal("Welcome to the guide. Code | Meaning", result.Chunks[0].Text);
        Assert.Equal("Details", result.Chunks[1].SectionTitle);
        Assert.Equal("More information.", result.Chunks[1].Text);
    }

    [Fact]
    public async Task ExtractsPdfText()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());

        var result = await processor.ProcessAsync(new DocumentInput("manual.pdf", new MemoryStream(CreatePdf("Hello from a PDF."))));

        Assert.Contains("Hello from a PDF.", result.Chunks.Single().Text);
    }

    [Fact]
    public async Task ExtractsPlainTextAndHeadersFromMultipartEmail()
    {
        const string email = """
            From: sender@example.com
            To: reader@example.com
            Subject: =?UTF-8?Q?Caf=C3=A9_update?=
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="mail-boundary"

            --mail-boundary
            Content-Type: text/plain; charset=utf-8
            Content-Transfer-Encoding: quoted-printable

            The update is ready.
            --mail-boundary
            Content-Type: text/html; charset=utf-8

            <p>HTML alternative</p>
            --mail-boundary--
            """;
        var processor = new DocumentProcessor(new DocumentProcessorOptions());

        var result = await processor.ProcessAsync(Input("update.eml", email));

        Assert.Equal("Café update", result.Sections.Single().Title);
        Assert.Contains("From: sender@example.com", result.Chunks.Single().Text);
        Assert.Contains("To: reader@example.com", result.Chunks.Single().Text);
        Assert.Contains("The update is ready.", result.Chunks.Single().Text);
        Assert.DoesNotContain("HTML alternative", result.Chunks.Single().Text);
    }

    [Fact]
    public async Task RejectsUnsupportedTypesAndEmptyDocuments()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions());

        await Assert.ThrowsAsync<UnsupportedDocumentTypeException>(() => processor.ProcessAsync(Input("video.mp4", "data")));
        await Assert.ThrowsAsync<DocumentProcessingException>(() => processor.ProcessAsync(Input("empty.txt", "  \n")));
    }

    [Fact]
    public async Task EnforcesInputSizeLimit()
    {
        var processor = new DocumentProcessor(new DocumentProcessorOptions { MaxDocumentBytes = 4 });

        var exception = await Assert.ThrowsAsync<DocumentProcessingException>(() => processor.ProcessAsync(Input("large.txt", "12345")));

        Assert.Contains("size limit", exception.Message);
    }

    [Fact]
    public void RegistersProcessorInDependencyInjection()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDocumentProcessing(options => options.MaxChunkCharacters = 200);
        using var provider = services.BuildServiceProvider();

        Assert.IsAssignableFrom<IDocumentProcessor>(provider.GetRequiredService<IDocumentProcessor>());
        Assert.Equal(200, provider.GetRequiredService<DocumentProcessorOptions>().MaxChunkCharacters);
    }

    private static DocumentInput Input(string fileName, string content) =>
        new(fileName, new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static byte[] CreateDocx(string documentXml)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(documentXml);
        }

        return output.ToArray();
    }

    private static byte[] CreatePdf(string text)
    {
        var streamContent = $"BT\n/F1 12 Tf\n72 720 Td\n({text}) Tj\nET\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(streamContent)} >>\nstream\n{streamContent}endstream"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var (body, index) in objects.Select((body, index) => (body, index)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{index + 1} 0 obj\n{body}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.Append($"{offset:0000000000} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}

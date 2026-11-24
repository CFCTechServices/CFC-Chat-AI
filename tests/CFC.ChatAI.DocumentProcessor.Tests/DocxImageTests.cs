using System.IO.Compression;
using System.Text;

namespace CFC.ChatAI.DocumentProcessor.Tests;

public sealed class DocxImageTests
{
    private const string Png = "<a:blip r:embed=\"png\"/>";
    private const string Gif = "<v:imagedata r:id=\"gif\"/>";
    private const string DuplicatePng = "<a:blip r:embed=\"copy\"/>";
    private const string DefaultPngTarget = "media/example.png";
    private static readonly byte[] GifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    [Fact]
    public async Task Images_follow_their_paragraph_or_table_and_reuse_identical_parts_across_chunks()
    {
        const string a = "aaaaaaaaaa.";
        const string b = "bbbbbbbbbb.";
        const string c = "cccccccccc.";
        var body = Para(Png) + Para($"<w:t>{a}</w:t>" + DuplicatePng + Gif) + Para($"<w:t>{b}</w:t>") +
            Para(Gif) + Para("<w:t>Third</w:t>", "Heading1") +
            $"<w:tbl><w:tr><w:tc>{Para(Png + $"<w:t>{c}</w:t>")}</w:tc></w:tr></w:tbl>";
        using var document = BuildDocx(body);
        var result = await Parse(document, maxChunkCharacters: 12);
        Assert.Collection(result.Images, image => Assert.Equal("image/png", image.ContentType),
            image => Assert.Equal("image/gif", image.ContentType));
        var png = result.Images[0].Id;
        var gif = result.Images[1].Id;
        Assert.Collection(result.Chunks,
            chunk => { Assert.Equal(a, chunk.Text); Assert.Equal(new[] { png, gif }, chunk.ImageIds); },
            chunk => { Assert.Equal(b, chunk.Text); Assert.Equal(new[] { gif }, chunk.ImageIds); },
            chunk => { Assert.Equal(c, chunk.Text); Assert.Equal("Third", chunk.SectionTitle); Assert.Equal(new[] { png }, chunk.ImageIds); });

        using var noImages = BuildDocx(body.Replace(Png, "").Replace(Gif, "").Replace(DuplicatePng, ""));
        var textOnly = await Parse(noImages, maxChunkCharacters: 12);
        Assert.Equal(result.Chunks.Select(c => c.Text), textOnly.Chunks.Select(c => c.Text));
        Assert.All(textOnly.Chunks, chunk => Assert.Empty(chunk.ImageIds));
    }

    [Fact]
    public async Task A_paragraph_split_across_chunks_keeps_its_image_with_its_last_piece()
    {
        using var document = BuildDocx(Para("<w:t>First bit.</w:t>" + Png + "<w:t> Final bit.</w:t>"));
        var result = await Parse(document, maxChunkCharacters: 12);
        Assert.Collection(result.Chunks,
            c => { Assert.Equal("First bit.", c.Text); Assert.Empty(c.ImageIds); },
            c => { Assert.Equal("Final bit.", c.Text); Assert.Single(c.ImageIds); });
    }

    [Theory]
    [InlineData("https://example.invalid/picture.png", "External", "image/png", false)]
    [InlineData("media/../document.xml", "Internal", "image/png", false)]
    [InlineData("media/%2E%2E/document.xml", "Internal", "image/png", false)]
    [InlineData("media/missing.png", "Internal", "image/png", false)]
    [InlineData(DefaultPngTarget, "Internal", "image/svg+xml", false)]
    [InlineData(DefaultPngTarget, "Internal", "image/gif", false)]
    [InlineData(DefaultPngTarget, "Internal", "image/png", true)]
    public async Task Invalid_or_unsupported_body_pictures_fail_the_upload(string target, string mode, string mime, bool corrupt)
    {
        using var document = BuildDocx(Para("<w:t>text</w:t>" + Png), target, mode, mime, corrupt ? [137, 80, 78, 71] : null);
        await Assert.ThrowsAsync<DocumentProcessingException>(() => Parse(document));
    }

    [Fact]
    public async Task Image_only_sections_and_linked_drawings_fail_explicitly()
    {
        using var onlyImage = BuildDocx(Para(Png) + Para("<w:t>Heading</w:t>", "Heading1") + Para("<w:t>Unrelated text.</w:t>"));
        Assert.Contains("no searchable text", (await Assert.ThrowsAsync<DocumentProcessingException>(() => Parse(onlyImage))).Message);
        using var linked = BuildDocx(Para("<w:t>text</w:t><a:blip r:link=\"png\"/>"));
        Assert.Contains("Linked", (await Assert.ThrowsAsync<DocumentProcessingException>(() => Parse(linked))).Message);
    }

    [Fact]
    public async Task Expansion_and_reference_limits_are_enforced()
    {
        using var compressed = BuildDocx(Para("<w:t>text</w:t>" + Png), png: new byte[8 * 1024 * 1024 + 1]);
        Assert.Contains("extraction limit", (await Assert.ThrowsAsync<DocumentProcessingException>(() => Parse(compressed))).Message);
        using var repeated = BuildDocx(Para("<w:t>text</w:t>" + string.Concat(Enumerable.Repeat(Png, 513))));
        Assert.Contains("too many image references", (await Assert.ThrowsAsync<DocumentProcessingException>(() => Parse(repeated))).Message);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocumentProcessor(new()).ProcessAsync(
            new DocumentInput("test.docx", repeated), cancel.Token));
    }

    private static string Para(string content, string? style = null) => $"<w:p>{(style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}<w:r>{content}</w:r></w:p>";

    private static Task<DocumentProcessingResult> Parse(Stream stream, int maxChunkCharacters = 1200) =>
        new DocumentProcessor(new() { MaxChunkCharacters = maxChunkCharacters }).ProcessAsync(new("test.docx", stream));

    private static MemoryStream BuildDocx(string body, string pngTarget = DefaultPngTarget, string targetMode = "Internal",
        string pngMime = "image/png", byte[]? png = null)
    {
        using var source = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "UploadParityFixtures", "embedded-image.docx"));
        using var pngSource = source.GetEntry("word/media/image1.png")!.Open();
        using var pngBuffer = new MemoryStream();
        pngSource.CopyTo(pngBuffer);
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:v="urn:schemas-microsoft-com:vml">
                  <w:body>{{body}}</w:body>
                </w:document>
                """);
            Add(zip, "word/_rels/document.xml.rels", $$"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="png" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" TargetMode="{{targetMode}}" Target="{{pngTarget}}"/>
                  <Relationship Id="gif" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="/word/media/example.gif"/>
                  <Relationship Id="copy" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/copy.png"/>
                </Relationships>
                """);
            Add(zip, "[Content_Types].xml", $$"""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="png" ContentType="{{pngMime}}"/><Override PartName="/word/media/example.gif" ContentType="image/gif"/>
                </Types>
                """);
            Add(zip, "word/media/example.png", png ?? pngBuffer.ToArray());
            Add(zip, "word/media/copy.png", pngBuffer.ToArray());
            Add(zip, "word/media/example.gif", GifBytes);
        }
        output.Position = 0;
        return output;
    }

    private static void Add(ZipArchive zip, string path, string text) => Add(zip, path, Encoding.UTF8.GetBytes(text));

    private static void Add(ZipArchive zip, string path, byte[] content)
    {
        using var stream = zip.CreateEntry(path).Open();
        stream.Write(content);
    }
}

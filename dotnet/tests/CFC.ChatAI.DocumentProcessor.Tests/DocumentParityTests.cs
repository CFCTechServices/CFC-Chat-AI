using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace CFC.ChatAI.DocumentProcessor.Tests;

// These records mirror the shape of document_reference.json.
public sealed record RefSection(string Title, int Level);
public sealed record RefChunk(string SectionTitle, string Text, int ImageCount);
public sealed record RefDocument(
    string File, bool Success, string? Error, string? DocId,
    List<RefSection>? Sections, List<RefChunk>? Chunks, string? FullText, int ImageCount);
public sealed record RefFile(string PythonPdfPath, bool OcrAvailable, List<RefDocument> Documents);

//TestData/ is gitignored, so only someone who generated the reference locally can run them, so skip these tests on machines that don't have the client documents
public sealed class RequiresDocumentReferenceTheoryAttribute : TheoryAttribute
{
    public static readonly string ReferencePath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "document_reference.json");

    public RequiresDocumentReferenceTheoryAttribute()
    {
        if (!File.Exists(ReferencePath))
            Skip = "Client documents aren't committed. Put them in TestData/documents and run "
                 + "tools/make_document_reference.py to enable the DocumentProcessor parity tests.";
    }
}

/// <summary>
/// Compares the C# DocumentProcessor with Python's app/services/document_processor.py.
/// Reference data: TestData/document_reference.json, made by tools/make_document_reference.py
/// </summary>
public class DocumentParityTests(ITestOutputHelper output)
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly RefFile Reference = JsonSerializer.Deserialize<RefFile>(
        File.ReadAllText(RequiresDocumentReferenceTheoryAttribute.ReferencePath),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    // Gives xUnit the list of file names, so each test runs once per document.
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var doc in Reference.Documents)
        {
            data.Add(doc.File);
        }
        return data;
    }

    private static RefDocument Expected(string file) => Reference.Documents.Single(d => d.File == file);

    private static string Norm(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static async Task<DocumentProcessingResult> Run(string file)
    {
        var options = new DocumentProcessorOptions();
        var soffice = Environment.GetEnvironmentVariable("SOFFICE_PATH");
        if (!string.IsNullOrWhiteSpace(soffice))
        {
            options.LibreOfficePath = soffice;
        }

        await using var stream = File.OpenRead(Path.Combine(TestData, "documents", file));
        var processor = new DocumentProcessor(options);
        return await processor.ProcessAsync(new DocumentInput(file, stream));
    }

    [RequiresDocumentReferenceTheory, MemberData(nameof(Files))]
    public async Task FullTextMatchesPython(string file)
    {
        var expected = Expected(file);
        if (!expected.Success)
        {
            // Python couldn't process it, so C# should refuse it too.
            await Assert.ThrowsAnyAsync<Exception>(() => Run(file));
            return;
        }

        var result = await Run(file);
        Assert.Equal(expected.DocId, result.DocumentId);

        var actual = Norm(string.Join(" ", result.Chunks.Select(c => c.Text)));
        AssertTextEqual(expected.FullText!, actual);
    }

    [RequiresDocumentReferenceTheory, MemberData(nameof(Files))]
    public async Task SectionsMatchPython(string file)
    {
        var expected = Expected(file);
        if (!expected.Success)
        {
            return;
        }

        var result = await Run(file);
        var actual = result.Sections.Select(s => new RefSection(s.Title, s.Level)).ToList();
        Assert.Equal(expected.Sections!, actual);
    }

    [RequiresDocumentReferenceTheory, MemberData(nameof(Files))]
    public async Task ReportChunkDifferences(string file)
    {
        var expected = Expected(file);
        if (!expected.Success)
        {
            return;
        }

        var result = await Run(file);
        output.WriteLine($"Python: {expected.Chunks!.Count} chunks, {expected.ImageCount} images, longest chunk {expected.Chunks.Max(c => c.Text.Length)} chars");
        output.WriteLine($"C#:     {result.Chunks.Count} chunks, 0 images (no image support), longest chunk {result.Chunks.Max(c => c.Text.Length)} chars");
    }

    // A normal Assert.Equal on two 40,000-character strings prints something unreadable.
    // This finds the first character where they differ and shows the text around it.
    private static void AssertTextEqual(string expected, string actual)
    {
        var i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i])
        {
            i++;
        }

        if (i == expected.Length && i == actual.Length)
        {
            return;
        }

        var start = Math.Max(0, i - 60);
        Assert.Fail($"""
            Text differs at character {i} (Python length {expected.Length}, C# length {actual.Length})
            Python: ...{expected.Substring(start, Math.Min(120, expected.Length - start))}...
            C#:     ...{actual.Substring(start, Math.Min(120, actual.Length - start))}...
            """);
    }
}
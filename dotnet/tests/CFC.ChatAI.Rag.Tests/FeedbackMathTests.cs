using System.Text.Json;

namespace CFC.ChatAI.Rag.Tests;

/// <summary>
/// FeedbackMath.Rerank must reproduce Python FeedbackService.rerank() exactly.
/// Expected values come from tools/make_rerank_reference.py (TestData/feedback_rerank_reference.json).
/// </summary>
public class FeedbackMathTests
{
    private record CaseSettings(double Alpha, double Scale, double AlphaQuery, double ScaleQuery);
    private record CaseChunk(string? ChunkId, double Score);
    private record ExpectedChunk(string? ChunkId, int Rank, double AdjustedScore);
    private record RerankCase(string Name, CaseSettings Settings, List<CaseChunk> Chunks,
                              Dictionary<string, int> Global, Dictionary<string, double> Query,
                              List<ExpectedChunk> Expected);

    private static List<RerankCase> LoadCases() =>
        JsonSerializer.Deserialize<List<RerankCase>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "feedback_rerank_reference.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    public static IEnumerable<object[]> CaseNames() => LoadCases().Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Rerank_MatchesPython(string caseName)
    {
        var c = LoadCases().Single(x => x.Name == caseName);
        var settings = new FeedbackSettings
        {
            Alpha = c.Settings.Alpha, Scale = c.Settings.Scale,
            AlphaQuery = c.Settings.AlphaQuery, ScaleQuery = c.Settings.ScaleQuery,
        };
        var chunks = c.Chunks
            .Select((chunk, i) => new ContextChunk { Rank = i + 1, ChunkId = chunk.ChunkId, Score = chunk.Score })
            .ToList();

        var result = FeedbackMath.Rerank(chunks, c.Global, c.Query, settings);

        Assert.Equal(c.Expected.Select(e => e.ChunkId), result.Select(r => r.ChunkId));   // same order
        Assert.Equal(c.Expected.Select(e => e.Rank), result.Select(r => r.Rank));         // renumbered ranks
        for (int i = 0; i < result.Count; i++)
            Assert.Equal(c.Expected[i].AdjustedScore, result[i].AdjustedScore!.Value, precision: 12);
    }

    [Fact]
    public void Rerank_KeepsOriginalPineconeScore()
    {
        var chunks = new List<ContextChunk> { new() { Rank = 1, ChunkId = "a", Score = 0.6 } };

        var result = FeedbackMath.Rerank(chunks, new Dictionary<string, int> { ["a"] = 5 },
                                         new Dictionary<string, double>(), new FeedbackSettings());

        Assert.Equal(0.6, result[0].Score);                 // Score untouched, like Python
        Assert.True(result[0].AdjustedScore > 0.6);         // the boost goes into AdjustedScore
        Assert.Null(chunks[0].AdjustedScore);               // the input chunk wasn't modified
    }
}
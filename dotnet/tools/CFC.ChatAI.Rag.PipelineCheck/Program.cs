using System.Text.Json;
using CFC.ChatAI.Pinecone.Internal;

// Live parity check for the full C# RAG pipeline (read-only).
// Runs the questions recorded by tools/make_pipeline_reference.py through RagPipelines.Documents
// and compares chunk order, every merged field, and the formatted context string with Python's.
// Run from the dotnet/ folder:  dotnet run --project tools/CFC.ChatAI.Rag.PipelineCheck

var env = LoadDotEnv(Path.Combine("..", ".env"));
string? Setting(string key) => Environment.GetEnvironmentVariable(key) ?? env.GetValueOrDefault(key);

var settings = RagSettings.FromEnvironment(Setting);
using var http = new HttpClient();
using var pipelines = RagPipelines.Create(settings, http);

var references = JsonSerializer.Deserialize<List<PipelineReference>>(
    File.ReadAllText("tests/CFC.ChatAI.Rag.Tests/TestData/pipeline_reference.json"),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

int passCount = 0, tieCount = 0, failCount = 0;
foreach (var reference in references)
{
    var chunks = await pipelines.Documents.RetrieveContextAsync(reference.Question, topK: reference.Chunks.Count);
    var context = RagPipeline.FormatContext(chunks);

    var problems = new List<string>();

    // 1) Same chunks in the same order (ties allowed, like VectorStoreCheck)
    string order = CompareOrder(reference.Chunks, chunks);
    if (order.StartsWith("FAIL")) problems.Add(order);

    // 2) Every chunk both sides returned must have identical merged fields
    var pythonById = reference.Chunks.Where(c => c.ChunkId is not null).ToDictionary(c => c.ChunkId!);
    foreach (var chunk in chunks)
    {
        if (chunk.ChunkId is null || !pythonById.TryGetValue(chunk.ChunkId, out var py))
            continue;   // a tied chunk swapped in at the cutoff; nothing to compare it with

        CheckField(problems, chunk.ChunkId, "text", py.Text, chunk.Text);
        CheckField(problems, chunk.ChunkId, "source", py.Source, chunk.Source);
        CheckField(problems, chunk.ChunkId, "source_type", py.SourceType, chunk.SourceType);
        CheckField(problems, chunk.ChunkId, "doc_id", py.DocId, chunk.DocId);
        CheckField(problems, chunk.ChunkId, "section_id", py.SectionId, chunk.SectionId);
        CheckField(problems, chunk.ChunkId, "section_title", py.SectionTitle, chunk.SectionTitle);
    }

    // 3) The context string can only match exactly when the chunks came back in the same order
    if (order == "PASS" && context != reference.Context)
        problems.Add($"FAIL context differs at character {FirstDifference(reference.Context, context)}");

    string verdict = problems.Count > 0 ? "FAIL" : order;
    if (verdict == "PASS") passCount++;
    else if (verdict == "TIE") tieCount++;
    else failCount++;

    Console.WriteLine($"{verdict,-6} {reference.Question}");
    foreach (var problem in problems) Console.WriteLine($"    {problem}");
}

Console.WriteLine($"\n{references.Count} questions | PASS {passCount} | TIE {tieCount} | FAIL {failCount}");

// ---- helpers ----

static void CheckField(List<string> problems, string chunkId, string field, string? expected, string? actual)
{
    if (expected != actual)
        problems.Add($"FAIL {field} differs for {chunkId[..8]}: Python \"{Shorten(expected)}\" vs C# \"{Shorten(actual)}\"");
}

static string Shorten(string? value) =>
    value is null ? "null" : value.Length <= 40 ? value : value[..40] + "...";

// PASS = identical IDs in identical order. TIE = differences only among exactly tied scores. FAIL = anything else.
static string CompareOrder(List<ReferenceChunk> expected, IReadOnlyList<ContextChunk> actual)
{
    if (expected.Count != actual.Count) return $"FAIL count: C# {actual.Count} vs Python {expected.Count}";
    if (expected.Select(e => e.ChunkId).SequenceEqual(actual.Select(a => a.ChunkId))) return "PASS";

    for (int i = 0; i < expected.Count; i++)
    {
        if (Math.Abs(expected[i].Score - actual[i].Score) > 1e-4) return $"FAIL score at rank {i + 1}";

        bool isTied = expected.Count(e => Math.Abs(e.Score - expected[i].Score) < 1e-6) >= 2;
        if (expected[i].ChunkId != actual[i].ChunkId && !isTied) return $"FAIL chunk at rank {i + 1}";
    }
    return "TIE";
}

static int FirstDifference(string a, string b)
{
    int shorter = Math.Min(a.Length, b.Length);
    for (int i = 0; i < shorter; i++)
        if (a[i] != b[i]) return i;
    return shorter;
}

// Minimal .env reader: KEY=VALUE lines, # comments, optional quotes (same as VectorStoreCheck).
static Dictionary<string, string> LoadDotEnv(string path)
{
    var values = new Dictionary<string, string>();
    if (!File.Exists(path)) return values;

    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        int eq = line.IndexOf('=');
        if (eq <= 0) continue;
        values[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
    }
    return values;
}

record ReferenceChunk(int Rank, double Score, string? ChunkId, string Text, string Source, string SourceType,
                      string? DocId, string? SectionId, string? SectionTitle);

record PipelineReference(string Question, List<ReferenceChunk> Chunks, string Context);
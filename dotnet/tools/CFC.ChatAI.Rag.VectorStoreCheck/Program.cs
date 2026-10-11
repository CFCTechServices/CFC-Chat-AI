using System.Text.Json;
using CFC.ChatAI.Rag;

// Read-only parity check for PineconeVectorStore
// Replays the exact query vectors Python used (retrieval_reference.json) and compares
// chunk IDs and scores. Credentials are read from the repo's .env (never printed)
// Run from the dotnet/ folder : dotnet run --project tools/CFC.ChatAI.Rag.VectorStoreCheck

var env = LoadDotEnv(Path.Combine("..",".env"));
string Setting(string key) => 
    Environment.GetEnvironmentVariable(key) ?? (env.TryGetValue(key, out var value) ? value: "" );

var store = new PineconeVectorStore(
    Setting("PINECONE_API_KEY"), Setting("PINECONE_INDEX_NAME"), Setting("PINECONE_NAMESPACE"));

var references = JsonSerializer.Deserialize<List<RetrievalReference>>(
    File.ReadAllText("tests/CFC.ChatAI.Rag.Tests/TestData/retrieval_reference.json"),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

int passCount = 0, tieCount = 0, failCount = 0;
foreach (var reference in references)
{
    var actual = await store.QueryAsync(reference.QueryVector, topK: reference.Matches.Count, includeMetadata: false);
    var verdict = Compare(reference.Matches, actual);

    if (verdict.StartsWith("PASS")) passCount++;
    else if (verdict.StartsWith("TIE")) tieCount++;
    else failCount++;

    Console.WriteLine($"{verdict, -12} {reference.Question}");
    if (!verdict.StartsWith("PASS"))
    {
        Console.WriteLine($"    Python: {string.Join("  ", reference.Matches.Select(m => $"{m.Id[..8]}({m.Score:F6})"))}");
        Console.WriteLine($"    C#    : {string.Join("  ", actual.Select(m => $"{m.Id[..8]}({m.Score:F6})"))}");
    }
}

Console.WriteLine($"\n{references.Count} questions | PASS {passCount} | TIE {tieCount} | FAIL {failCount}");

// PASS = identical IDs in identical order.
// TIE  = same scores at every rank; any differing chunks have scores tied with another chunk.
// FAIL = anything else.
static string Compare(List<ReferenceMatch> expected, IReadOnlyList<VectorMatch> actual)
{
    if (expected.Count != actual.Count) return "FAIL count";
    if (expected.Select(e => e.Id).SequenceEqual(actual.Select(a => a.Id))) return "PASS";

    for (int i = 0; i < expected.Count; i++)
    {
        if (Math.Abs(expected[i].Score - actual[i].Score) > 1e-4) return "FAIL score";

        bool isTied = expected.Count(e => Math.Abs(e.Score - expected[i].Score) < 1e-6) >= 2;
        if (expected[i].Id != actual[i].Id && !isTied) return "FAIL chunk";
    }
    return "TIE";
}

// Minimal .env reader: KEY=VALUE lines, # comments, optional quotes.
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

record ReferenceMatch(string Id, double Score);
record RetrievalReference(string Question, float[] QueryVector, List<ReferenceMatch> Matches);
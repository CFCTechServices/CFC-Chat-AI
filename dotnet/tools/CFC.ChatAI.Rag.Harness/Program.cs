using System.Text.Json;
using CFC.ChatAI.Rag;

using var model = new OnnxEmbeddingModel("models/model.onnx", "models/vocab.txt");

var json = File.ReadAllText("tests/CFC.ChatAI.Rag.Tests/TestData/reference_embeddings.json");
var refs = JsonSerializer.Deserialize<List<ReferenceEmbedding>>(
    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

bool failed = false;
Console.WriteLine("cosine    text");
foreach (var r in refs)
{
    var v = model.EncodeQuery(r.Text);
    double dot = 0, a = 0, b = 0;
    for (int i = 0; i < v.Length; i++)
    {
        dot += v[i] * r.Embedding[i];
        a += v[i] * v[i];
        b += r.Embedding[i] * r.Embedding[i];
    }
    double cosine = dot / (Math.Sqrt(a) * Math.Sqrt(b));
    bool compatible = v.Length == OnnxEmbeddingModel.Dimension && v.All(float.IsFinite) && double.IsFinite(cosine) && cosine >= 0.9999;
    failed |= !compatible;
    string status = compatible ? "PASS" : "FAIL";
    Console.WriteLine($"{cosine:F6}  {status}  {r.Text}");
}

var first = model.EncodeQuery(refs[0].Text);
Console.WriteLine($"\ndims={first.Length}");
Console.WriteLine($"C#     first5: [{string.Join(", ", first.Take(5).Select(x => x.ToString("F6")))}]");
Console.WriteLine($"Python first5: [{string.Join(", ", refs[0].Embedding.Take(5).Select(x => x.ToString("F6")))}]");

Environment.ExitCode = failed ? 1 : 0;

record ReferenceEmbedding(string Text, float[] Embedding);
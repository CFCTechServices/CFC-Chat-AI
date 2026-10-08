using System.Text.Json;
using CFC.ChatAI.Rag.Internal;

namespace CFC.ChatAI.Rag.Tests;

public sealed class OnnxIntegrationFactAttribute : FactAttribute
{
    public OnnxIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_ONNX_INTEGRATION") != "1")
            Skip = "Opt-in: set RUN_ONNX_INTEGRATION=1 and ONNX_TEST_MODEL_PATH/ONNX_TEST_VOCAB_PATH.";
    }
}

public sealed class OnnxEmbeddingCompatibilityTests
{
    [OnnxIntegrationFact]
    [Trait("Category", "Integration")]
    public void ReferenceEmbeddingsRetainPythonParity()
    {
        using var model = new OnnxEmbeddingModel(Required("ONNX_TEST_MODEL_PATH"), Required("ONNX_TEST_VOCAB_PATH"));
        var references = JsonSerializer.Deserialize<List<ReferenceEmbedding>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "reference_embeddings.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var vector = model.EncodeQuery(reference.Text);
            EmbeddingValidation.Validate(vector);
            Assert.Equal(384, vector.Length);
            double dot = vector.Zip(reference.Embedding).Sum(pair => (double)pair.First * pair.Second);
            double norm = Math.Sqrt(vector.Sum(v => (double)v * v) * reference.Embedding.Sum(v => (double)v * v));
            Assert.True(dot / norm >= 0.9999, $"Embedding parity failed: cosine={dot / norm}");
        }
    }
    internal static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing test setting {name}.");
    private sealed record ReferenceEmbedding(string Text, float[] Embedding);
}

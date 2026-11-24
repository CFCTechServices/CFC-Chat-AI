using CFC.ChatAI.Pinecone.Internal;
namespace CFC.ChatAI.Pinecone.Tests;

public class RagSettingsTests
{
    // Builds a fake environment from key/value pairs.
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return key => map.GetValueOrDefault(key);
    }

    private static readonly (string Key, string Value)[] RequiredOnly =
    [
        ("PINECONE_API_KEY", "pinecone-key"),
        ("SUPABASE_URL", "https://example.supabase.co"),
        ("SUPABASE_SERVICE_ROLE_KEY", "service-key"),
    ];

    [Fact]
    public void RequiredOnly_UsesPythonDefaults()
    {
        var settings = RagSettings.FromEnvironment(Env(RequiredOnly));

        Assert.Equal("cfc-rag-chatbot", settings.PineconeIndexName);
        Assert.Equal("cfc-rag-chatbot", settings.PineconeVideoIndexName);   // falls back to the document index
        Assert.Null(settings.PineconeNamespace);
        Assert.Equal(Path.Combine("models", "model.onnx"), settings.ModelPath);
        Assert.Equal(Path.Combine("models", "vocab.txt"), settings.VocabPath);

    }

    [Fact]
    public void VideoIndex_FollowsPythonFallbackOrder()
    {
        var onlySecondChoice = RagSettings.FromEnvironment(Env([.. RequiredOnly, ("PINECONE_INDEX_NAME_VIDEOS", "videos-b")]));
        var firstChoiceWins = RagSettings.FromEnvironment(Env([.. RequiredOnly,
            ("PINECONE_VIDEO_INDEX_NAME", "videos-a"), ("PINECONE_INDEX_NAME_VIDEOS", "videos-b")]));

        Assert.Equal("videos-b", onlySecondChoice.PineconeVideoIndexName);
        Assert.Equal("videos-a", firstChoiceWins.PineconeVideoIndexName);
    }

    [Fact]
    public void BlankValues_CountAsMissing_LikePythonOr()
    {
        var settings = RagSettings.FromEnvironment(Env([.. RequiredOnly,
            ("PINECONE_INDEX_NAME", ""), ("PINECONE_NAMESPACE", "   ")]));

        Assert.Equal("cfc-rag-chatbot", settings.PineconeIndexName);
        Assert.Null(settings.PineconeNamespace);
    }

    [Theory]
    [InlineData("PINECONE_API_KEY")]
    [InlineData("SUPABASE_URL")]
    [InlineData("SUPABASE_SERVICE_ROLE_KEY")]
    public void MissingRequiredSetting_ThrowsWithItsName(string missingKey)
    {
        var env = Env(RequiredOnly.Where(v => v.Key != missingKey).ToArray());

        var error = Assert.Throws<InvalidOperationException>(() => RagSettings.FromEnvironment(env));
        Assert.Contains(missingKey, error.Message);
    }

    [RequiresEmbeddingModelFact]
    public void RagPipelines_Create_BuildsBothPipelinesWithTheRealModel()
    {
        var settings = RagSettings.FromEnvironment(Env([.. RequiredOnly,
            ("ONNX_MODEL_PATH", Path.Combine(AppContext.BaseDirectory, "models", "model.onnx")),
            ("ONNX_VOCAB_PATH", Path.Combine(AppContext.BaseDirectory, "models", "vocab.txt"))]));

        using var pipelines = RagPipelines.Create(settings, new HttpClient());

        Assert.NotNull(pipelines.Documents);
        Assert.NotNull(pipelines.Videos);
        Assert.NotSame(pipelines.Documents, pipelines.Videos);
    }
}
namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>
/// Settings for the RAG pipelines. Same environmetn variable names, defaults and fallbacks as 
/// app/config.py, so the existing .env works unchanged
/// </summary>

internal sealed record RagSettings
{
    public const string DefaultIndexName = "cfc-rag-chatbot";
    public static readonly string DefaultModelPath = Path.Combine("models", "model.onnx");
    public static readonly string DefaultVocabPath = Path.Combine("models", "vocab.txt");

    public required string PineconeApiKey { get; init; }
    public string PineconeIndexName { get; init; } = DefaultIndexName;
    public required string PineconeVideoIndexName { get; init; }
    public string? PineconeNamespace { get; init; }

    public required string SupabaseUrl { get; init; }
    public required string SupabaseServiceRoleKey { get; init; }

    public string ModelPath { get; init; } = DefaultModelPath;
    public string VocabPath { get; init; } = DefaultVocabPath;

    /// <summary>
    /// reads settings through any lookup functionm, e.g. Environment.GetEnvironmentVariable, or a dictionary lookup, etc.
    /// Blank values count as missing, like python's 'os.getenv(...) or ...'.
    /// </summary>
    public static RagSettings FromEnvironment(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        string? Get(string key) => string.IsNullOrWhiteSpace(get(key)) ? null : get(key)!.Trim();
        string Required(string key) => Get(key) ?? throw new InvalidOperationException($"{key} is not set.");

        string indexName = Get("PINECONE_INDEX_NAME") ?? DefaultIndexName;

        return new RagSettings
        {
            PineconeApiKey = Required("PINECONE_API_KEY"),
            PineconeIndexName = indexName,
            PineconeVideoIndexName = Get("PINECONE_VIDEO_INDEX_NAME")
                                     ?? Get("PINECONE_INDEX_NAME_VIDEOS")
                                     ?? Get("PINECONE_INDEX")
                                     ?? indexName,
            PineconeNamespace = Get("PINECONE_NAMESPACE"),
            SupabaseUrl = Required("SUPABASE_URL"),
            SupabaseServiceRoleKey = Required("SUPABASE_SERVICE_ROLE_KEY"),
            ModelPath = Get("ONNX_MODEL_PATH") ?? DefaultModelPath,
            VocabPath = Get("ONNX_VOCAB_PATH") ?? DefaultVocabPath,
        };
    }
}
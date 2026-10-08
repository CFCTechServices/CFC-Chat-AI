namespace CFC.ChatAI.Api.Configuration;

/// <summary>Diagnostics do not require the ingestion/RAG configuration or load a model at startup.</summary>
public sealed class RuntimeOptions
{
    public string? PineconeApiKey { get; init; }
    public string PineconeIndexName { get; init; } = "cfc-rag-chatbot";
    public string? SupabaseUrl { get; init; }
    public string? SupabaseAnonKey { get; init; }
    public string? SupabaseServiceRoleKey { get; init; }
    public string? AzureApiKey { get; init; }
    public string? AzureEndpoint { get; init; }
    public string? AzureDeployment { get; init; }
    public string EmbeddingModelName { get; init; } = "sentence-transformers/all-MiniLM-L6-v2";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public static RuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        string? Value(string key, string legacy) => configuration[key] ?? configuration[legacy];
        var seconds = configuration.GetValue("Diagnostics:RequestTimeoutSeconds", 5d);
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 60)
            throw new InvalidOperationException("Diagnostics timeout must be between 0 and 60 seconds.");
        return new RuntimeOptions
        {
            PineconeApiKey = Value("Pinecone:ApiKey", "PINECONE_API_KEY"),
            PineconeIndexName = Value("Pinecone:Documents:IndexName", "PINECONE_INDEX_NAME") ?? "cfc-rag-chatbot",
            SupabaseUrl = Value("Supabase:Url", "SUPABASE_URL"),
            SupabaseAnonKey = Value("Supabase:AnonKey", "SUPABASE_ANON_KEY"),
            SupabaseServiceRoleKey = Value("Supabase:ServiceRoleKey", "SUPABASE_SERVICE_ROLE_KEY"),
            AzureApiKey = Value("AzureOpenAI:ApiKey", "AZURE_OPENAI_API_KEY"),
            AzureEndpoint = Value("AzureOpenAI:Endpoint", "AZURE_OPENAI_ENDPOINT"),
            AzureDeployment = Value("AzureOpenAI:Deployment", "AZURE_OPENAI_DEPLOYMENT"),
            EmbeddingModelName = Value("Embedding:ModelName", "EMBED_MODEL_NAME") ?? "sentence-transformers/all-MiniLM-L6-v2",
            RequestTimeout = TimeSpan.FromSeconds(seconds)
        };
    }
}

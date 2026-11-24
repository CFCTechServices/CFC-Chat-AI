namespace CFC.ChatAI.Pinecone;

/// <summary>Connection and model asset settings for the Pinecone infrastructure module.</summary>
public sealed class PineconeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public string ModelPath { get; set; } = string.Empty;
    public string VocabularyPath { get; set; } = string.Empty;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Pinecone ApiKey is required.");
        if (string.IsNullOrWhiteSpace(IndexName))
            throw new InvalidOperationException("Pinecone IndexName is required.");
        if (string.IsNullOrWhiteSpace(ModelPath))
            throw new InvalidOperationException("Pinecone ModelPath is required.");
        if (string.IsNullOrWhiteSpace(VocabularyPath))
            throw new InvalidOperationException("Pinecone VocabularyPath is required.");
    }
}

namespace CFC.ChatAI.Pinecone.Internal;

/// <summary>
/// Turns text into embedding vectors. Implemented by <see cref="OnnxEmbeddingModel"/>.
/// Exists so callers such as <see cref="TextVectorStore"/> can be unit-tested with a fake.
/// </summary>
internal interface IEmbeddingModel
{
    /// <summary>Equivalent of Python encode_query().</summary>
    float[] EncodeQuery(string query);

    /// <summary>Equivalent of Python encode(texts).</summary>
    List<float[]> Encode(IReadOnlyList<string> texts);
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace CFC.Rag;

/// <summary>
/// C# port of app/core/embeddings.py: all-MiniLM-L6-v2 run via ONNX Runtime.
/// Mirrors sentence-transformers: BERT tokenize -> transformer -> mean pooling -> L2 normalization.
/// </summary>

public sealed class OnnxEmbeddingModel : IDisposable
{
    public const int Dimension = 384;
    private const int MaxTokens = 256; // models max sequence length
    
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    public OnnxEmbeddingModel(string modelPath, string vocabPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions { LowerCaseBeforeTokenization = true });
    }

    /// <summary> Equivalent of Python encode_query().</summary>
    public float[] EncodeQuery(string query) => EmbedOne(query);

    /// <summary> Equivalent of Python encode(texts).</summary>
    public List<float[]> Encode(IReadOnlyList<string> texts) => texts.Select(EmbedOne).ToList();

    private float[] EmbedOne(string text)
    {
        // Tokenize; make sure the sequence is wrapped in [CLS] ... [SEP]
        // (done manually so it works regardless of tokenizer version defaults)
        var ids = _tokenizer.EncodeToIds(text).ToList();
        int cls = _tokenizer.ClassificationTokenId;
        int sep = _tokenizer.SeparatorTokenId;
        if (ids.Count == 0 || ids[0] != cls) ids.Insert(0, cls);
        if (ids[^1] != sep) ids.Add(sep);
        if (ids.Count > MaxTokens )
        {
            ids = ids.Take(MaxTokens - 1).ToList();
            ids.Add(sep);
        }

        int n = ids.Count;
        int [] shape = [1, n];
        long [] inputIds = ids.Select(i => (long)i).ToArray();
        long [] attentionMask = Enumerable.Repeat(1L, n).ToArray();
        long [] tokenTypeIds = new long[n];

        // Only feed the inputs this particular ONNX export actually declares
        var inputs = new List<NamedOnnxValue>();
        foreach (var name in _session.InputMetadata.Keys)
        {
            long[] data = name switch
            {
                "input_ids" => inputIds,
                "attention_mask" => attentionMask,
                "token_type_ids" => tokenTypeIds,
                _ => throw new InvalidOperationException($"Unexpected model input: {name}")
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, shape)));
        }
        
        using var results = _session.Run(inputs);
        var hidden = results.First().AsTensor<float>(); // [1, tokens, 384]
        int dim = hidden.Dimensions[2];

        // mean pooling over tokens
        var v = new float[dim];
        for (int t = 0; t < n; t++)
            for (int d = 0; d < dim; d++)
                v[d] += hidden[0,t,d];
        for (int d = 0; d < dim; d++) v[d] /= n;

        // L2 normalize
        float norm = MathF.Sqrt(v.Sum(x => x * x ));
        for (int d = 0; d < dim; d++) v[d] /= norm;

        return v;
    }

    public void Dispose() => _session.Dispose();
    
}
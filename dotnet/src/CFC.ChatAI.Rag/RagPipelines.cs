using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Rag;

/// <summary>
/// The document and video RAG pipelines. built the way app/services/chat_service.py builds them:
/// one shared embedding model, and the video pipeline reuses the document vector store when both
/// point at the same Pinecone index. create one at startup and keep it for the app's lifetime
/// </summary>

 public sealed class RagPipelines : IDisposable
{
    private readonly OnnxEmbeddingModel _model;

    public RagPipeline Documents { get; }
    public RagPipeline Videos { get; }

    private RagPipelines(OnnxEmbeddingModel model, RagPipeline documents, RagPipeline videos)
    {
        _model = model;
        Documents = documents;
        Videos = videos;
    }

    public static RagPipelines Create(
        RagSettings settings,
        HttpClient http,
        IFeedbackReranker? reranker = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(http);

        // One model for both pipelines: it's a large file and slow to load (same as Python).
        var model = new OnnxEmbeddingModel(settings.ModelPath, settings.VocabPath);

        var chunks = new SupabaseChunkRepository(http, settings.SupabaseUrl, settings.SupabaseServiceRoleKey);
        var logger = loggerFactory?.CreateLogger<RagPipeline>();

        var documentStore = new PineconeVectorStore(
            settings.PineconeApiKey, settings.PineconeIndexName, settings.PineconeNamespace);

        // Same index name -> reuse the same store, like chat_service.py.
        var videoStore = settings.PineconeVideoIndexName == settings.PineconeIndexName
            ? documentStore
            : new PineconeVectorStore(settings.PineconeApiKey, settings.PineconeVideoIndexName, settings.PineconeNamespace);

        var documents = new RagPipeline(new TextVectorStore(model, documentStore), chunks, reranker, logger);
        var videos = new RagPipeline(new TextVectorStore(model, videoStore), chunks, reranker, logger);

        return new RagPipelines(model, documents, videos);
    }

    /// <summary>Releases the ONNX model (native memory). Call once, when the app shuts down.</summary>
    public void Dispose() => _model.Dispose();
}
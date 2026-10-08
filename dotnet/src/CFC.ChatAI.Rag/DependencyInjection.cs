using System.Globalization;
using CFC.ChatAI.Rag.Configuration;
using CFC.ChatAI.Rag.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.Rag;

public static class DependencyInjection
{
    /// <summary>Registers a single long-lived model owner, disposed by DI. No network I/O at registration.</summary>
    public static IServiceCollection AddPineconeService(this IServiceCollection services, PineconeServiceOptions options, EmbeddingOptions embedding)
    {
        options.Validate(embedding);
        // Snapshot the only mutable options collection.
        options = options with { AllowedDocumentIds = options.AllowedDocumentIds is null ? null : Array.AsReadOnly(options.AllowedDocumentIds.ToArray()) };
        services.AddLogging();
        services.AddHttpClient("CFC.Pinecone", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RedactLoggedHeaders(_ => true)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddSingleton(options);
        services.AddSingleton(embedding);
        services.AddSingleton<IEmbeddingEncoder, OnnxEmbeddingEncoder>();
        services.AddSingleton<IPineconeTransport>(sp => new PineconeTransport(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("CFC.Pinecone"),
            sp.GetRequiredService<PineconeServiceOptions>(), sp.GetRequiredService<ILogger<PineconeTransport>>()));
        services.AddSingleton<IPineconeService>(sp => new PineconeService(
            sp.GetRequiredService<PineconeServiceOptions>(), sp.GetRequiredService<EmbeddingOptions>(),
            sp.GetRequiredService<IEmbeddingEncoder>(), sp.GetRequiredService<IPineconeTransport>(), sp.GetRequiredService<ILogger<PineconeService>>()));
        return services;
    }

    /// <summary>Maps legacy environment keys explicitly. Relative asset paths are resolved against contentRootPath.</summary>
    public static IServiceCollection AddPineconeService(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        string? Value(string hierarchical, string legacy) => configuration[hierarchical] ?? configuration[legacy];
        string Required(string hierarchical, string legacy) => Value(hierarchical, legacy) ?? "";
        int Number(string key, int fallback)
        {
            if (configuration[key] is not { } value) return fallback;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
            throw new Contracts.PineconeServiceException(Contracts.PineconeErrorCategory.Configuration, "Invalid numeric Pinecone setting.");
        }
        var documentsName = Required("Pinecone:Documents:IndexName", "PINECONE_INDEX_NAME");
        var ns = Required("Pinecone:Documents:Namespace", "PINECONE_NAMESPACE");
        var documents = new PineconeTargetOptions
        {
            IndexName = documentsName, Host = Value("Pinecone:Documents:Host", "PINECONE_INDEX_HOST"), Namespace = ns,
            EmbeddingModelIdentity = Required("Pinecone:Documents:EmbeddingModelIdentity", "PINECONE_DOCUMENT_MODEL_IDENTITY")
        };
        var videos = new PineconeTargetOptions
        {
            IndexName = Value("Pinecone:Videos:IndexName", "PINECONE_VIDEO_INDEX_NAME") ?? documentsName,
            Host = Value("Pinecone:Videos:Host", "PINECONE_VIDEO_INDEX_HOST"),
            Namespace = Value("Pinecone:Videos:Namespace", "PINECONE_VIDEO_NAMESPACE") ?? ns,
            EmbeddingModelIdentity = Required("Pinecone:Videos:EmbeddingModelIdentity", "PINECONE_VIDEO_MODEL_IDENTITY")
        };
        return services.AddPineconeService(new PineconeServiceOptions
        {
            ApiKey = Required("Pinecone:ApiKey", "PINECONE_API_KEY"), Documents = documents, Videos = videos,
            RequestTimeout = TimeSpan.FromSeconds(Number("Pinecone:RequestTimeoutSeconds", 30)),
            BatchSize = Number("Pinecone:BatchSize", 100), MaxPayloadBytes = Number("Pinecone:MaxPayloadBytes", 1_900_000),
            MaxMetadataBytes = Number("Pinecone:MaxMetadataBytes", 40_000), MaxTopK = Number("Pinecone:MaxTopK", 100),
            MaxRetries = Number("Pinecone:MaxRetries", 3), RetryBaseDelay = TimeSpan.FromMilliseconds(Number("Pinecone:RetryBaseDelayMilliseconds", 200))
        }, new EmbeddingOptions
        {
            ModelPath = Path.GetFullPath(Value("Embedding:ModelPath", "ONNX_MODEL_PATH") ?? "models/model.onnx", contentRootPath),
            VocabularyPath = Path.GetFullPath(Value("Embedding:VocabularyPath", "ONNX_VOCAB_PATH") ?? "models/vocab.txt", contentRootPath),
            ModelIdentity = Required("Embedding:ModelIdentity", "EMBEDDING_MODEL_IDENTITY")
        });
    }
}

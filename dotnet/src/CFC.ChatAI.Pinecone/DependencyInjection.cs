using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Pinecone.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Pinecone;

/// <summary>Composition-root registration for Pinecone and its embedding model.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPineconeInfrastructure(
        this IServiceCollection services,
        Action<PineconeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PineconeOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IEmbeddingModel>(_ =>
            new OnnxEmbeddingModel(options.ModelPath, options.VocabularyPath));
        services.AddSingleton<IVectorStore>(_ =>
            new PineconeVectorStore(options.ApiKey, options.IndexName, options.Namespace));
        services.AddSingleton<PineconeService>(provider =>
            new PineconeService(
                provider.GetRequiredService<IEmbeddingModel>(),
                provider.GetRequiredService<IVectorStore>()));
        services.AddSingleton<IPineconeService>(provider => provider.GetRequiredService<PineconeService>());
        services.AddSingleton<IQueryEmbeddingService>(provider => provider.GetRequiredService<PineconeService>());
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Composition-root registration for document processing.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDocumentProcessing(
        this IServiceCollection services,
        Action<DocumentProcessorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DocumentProcessorOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();
        return services;
    }
}

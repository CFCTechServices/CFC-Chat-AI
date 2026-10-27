using CFC.ChatAI.DocumentHandler._Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.DocumentHandler;

/// <summary>Composition-root registration for document ingestion and lifecycle workflows.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDocumentHandling(
        this IServiceCollection services,
        Action<DocumentHandlerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DocumentHandlerOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.TryAddSingleton<IDocumentHandler, _Internal.DocumentHandler>();
        return services;
    }
}

using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Feedback;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Storage;
using CFC.ChatAI.Supabase.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Supabase;

/// <summary>Composition-root registration for the Supabase infrastructure module.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddSupabaseInfrastructure(
        this IServiceCollection services,
        Action<SupabaseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SupabaseOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddHttpClient("CFC.ChatAI.Supabase");
        services.AddSingleton(serviceProvider => new SupabaseTransport(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<SupabaseOptions>()));
        services.AddSingleton<IIdentityGateway>(serviceProvider =>
            new SupabaseIdentityGateway(serviceProvider.GetRequiredService<SupabaseTransport>()));
        services.AddSingleton<IAdminIdentityGateway>(serviceProvider =>
            new SupabaseAdminIdentityGateway(serviceProvider.GetRequiredService<SupabaseTransport>()));
        services.AddSingleton<IChunkRepository>(serviceProvider =>
            new SupabaseChunkRepository(serviceProvider.GetRequiredService<SupabaseTransport>()));
        services.AddSingleton<IFeedbackRepository>(serviceProvider =>
            new SupabaseFeedbackRepository(serviceProvider.GetRequiredService<SupabaseTransport>()));
        services.AddSingleton<IObjectStorage>(serviceProvider =>
            new SupabaseObjectStorage(
                serviceProvider.GetRequiredService<SupabaseTransport>(),
                serviceProvider.GetRequiredService<SupabaseOptions>()));
        return services;
    }
}

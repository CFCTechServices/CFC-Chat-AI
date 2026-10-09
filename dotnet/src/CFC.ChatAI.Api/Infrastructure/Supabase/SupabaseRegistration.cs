using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Services;
using CFC.ChatAI.Api.Configuration;

namespace CFC.ChatAI.Api.Infrastructure.Supabase;

public static class SupabaseRegistration
{
    public const string HttpClientName = "CFC.Supabase";
    public static IServiceCollection AddSupabaseInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(sp => SupabaseOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            // Default HttpClient loggers include the request URI (invitation/recovery PII).
            .RemoveAllLoggers().RedactLoggedHeaders(_ => true)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton<SupabaseTransport>();
        services.AddSingleton<IIdentityGateway, SupabaseIdentityGateway>();
        services.AddSingleton<IProfileRepository, SupabaseProfileRepository>();
        services.AddSingleton<IProfileAccessReader, SupabaseProfileAccessReader>();
        services.AddSingleton<IInvitationRepository, SupabaseInvitationRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RecoveryService>();
        services.AddScoped<EligibilityService>();
        services.AddScoped<ProfileService>();
        return services;
    }
}

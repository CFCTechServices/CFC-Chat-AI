using CFC.ChatAI.ChatService.Contracts;
using CFC.ChatAI.ChatService.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CFC.ChatAI.ChatService;

/// <summary>Composition-root registration for chat retrieval, generation, and conversation persistence.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddChatService(
        this IServiceCollection services,
        Action<ChatServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ChatServiceOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        if (options.IsAzureOpenAIConfigured)
        {
            services.TryAddSingleton<IChatCompletionClient>(
                _ => new AzureOpenAIChatCompletionClient(options));
        }

        services.TryAddSingleton<IChatService>(serviceProvider =>
        {
            return new Internal.ChatService(
                serviceProvider.GetRequiredService<ChatServiceOptions>(),
                serviceProvider.GetRequiredService<CFC.ChatAI.Supabase.Conversations.IConversationRepository>(),
                serviceProvider.GetRequiredService<CFC.ChatAI.Feedback.Contracts.IFeedbackService>(),
                serviceProvider.GetService<IChatCompletionClient>(),
                serviceProvider.GetRequiredService<ILogger<Internal.ChatService>>());
        });

        return services;
    }
}

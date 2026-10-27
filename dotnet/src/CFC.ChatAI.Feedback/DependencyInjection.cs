using CFC.ChatAI.Feedback.Contracts;
using CFC.ChatAI.Feedback.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.Feedback;

/// <summary>Composition-root registration for query search and feedback reranking.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFeedback(
        this IServiceCollection services,
        Action<FeedbackOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FeedbackOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IFeedbackService, FeedbackService>();
        return services;
    }
}

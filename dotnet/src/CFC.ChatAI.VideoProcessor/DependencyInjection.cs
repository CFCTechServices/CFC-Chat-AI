using System.ClientModel;
using Azure.AI.Speech.Transcription;
using CFC.ChatAI.VideoProcessor._Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.VideoProcessor;

/// <summary>Composition-root registration for video transcription.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddVideoProcessing(
        this IServiceCollection services,
        Action<VideoProcessorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new VideoProcessorOptions();
        configure(options);
        options.Validate();

        var clientOptions = new TranscriptionClientOptions
        {
            NetworkTimeout = options.TranscriptionTimeout
        };
        var client = new TranscriptionClient(
            options.SpeechEndpoint!,
            new ApiKeyCredential(options.SpeechApiKey!),
            clientOptions);

        services.AddSingleton(options);
        services.TryAddSingleton(client);
        services.TryAddSingleton<IAudioTranscriber, AzureSpeechTranscriber>();
        services.TryAddSingleton<IVideoAudioExtractor, FfmpegAudioExtractor>();
        services.TryAddSingleton<IVideoProcessor, _Internal.VideoProcessor>();
        return services;
    }
}

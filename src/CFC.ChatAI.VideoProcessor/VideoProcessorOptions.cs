namespace CFC.ChatAI.VideoProcessor;

/// <summary>Azure Speech and local media extraction settings for video processing.</summary>
public sealed class VideoProcessorOptions
{
    /// <summary>Azure AI Speech resource endpoint, for example https://eastus.api.cognitive.microsoft.com/.</summary>
    public Uri? SpeechEndpoint { get; set; }

    /// <summary>Azure AI Speech resource key. Prefer loading this value from a secret store.</summary>
    public string? SpeechApiKey { get; set; }

    /// <summary>Path to the FFmpeg executable used to extract an audio track.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Maximum accepted video upload size. Defaults to 500 MiB.</summary>
    public long MaxVideoBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>Maximum extracted WAV size sent to Azure Speech. Azure supports audio smaller than 250 MB.</summary>
    public long MaxAudioBytes { get; set; } = 240_000_000;

    /// <summary>Maximum duration accepted by the Speech transcription endpoint (which supports less than two hours).</summary>
    public TimeSpan MaxAudioDuration { get; set; } = TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(59));

    /// <summary>Maximum number of characters in each output text chunk.</summary>
    public int MaxChunkCharacters { get; set; } = 1200;

    /// <summary>Maximum time allowed to extract audio from one video.</summary>
    public TimeSpan AudioExtractionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Network timeout for one Azure Speech request.</summary>
    public TimeSpan TranscriptionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Optional locale candidates, such as en-US. Leave empty for Azure language auto-detection.</summary>
    public IList<string> Locales { get; } = new List<string>();

    /// <summary>Whether to request speaker diarization from Azure Speech.</summary>
    public bool EnableSpeakerDiarization { get; set; }

    /// <summary>Expected upper bound on speakers when diarization is enabled.</summary>
    public int? MaxSpeakers { get; set; }

    internal void Validate()
    {
        if (SpeechEndpoint is null || !SpeechEndpoint.IsAbsoluteUri
            || SpeechEndpoint.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("A valid absolute HTTP(S) Azure Speech endpoint is required.", nameof(SpeechEndpoint));
        }

        if (string.IsNullOrWhiteSpace(SpeechApiKey))
        {
            throw new ArgumentException("An Azure Speech API key is required.", nameof(SpeechApiKey));
        }

        if (string.IsNullOrWhiteSpace(FfmpegPath))
        {
            throw new ArgumentException("An FFmpeg executable path is required.", nameof(FfmpegPath));
        }

        if (MaxVideoBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxVideoBytes), "The video size limit must be positive.");
        }

        if (MaxAudioBytes <= 0 || MaxAudioBytes >= 250_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAudioBytes), "The audio size limit must be positive and below Azure Speech's 250 MB limit.");
        }

        if (MaxAudioDuration <= TimeSpan.Zero || MaxAudioDuration >= TimeSpan.FromHours(2))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAudioDuration), "The audio duration limit must be positive and less than two hours.");
        }

        if (MaxChunkCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChunkCharacters), "The chunk size must be positive.");
        }

        if (AudioExtractionTimeout <= TimeSpan.Zero || TranscriptionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AudioExtractionTimeout), "Processing timeouts must be positive.");
        }

        if (MaxSpeakers is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSpeakers), "The maximum speaker count must be positive when specified.");
        }

        if (Locales.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Locale values must not be empty.", nameof(Locales));
        }
    }
}

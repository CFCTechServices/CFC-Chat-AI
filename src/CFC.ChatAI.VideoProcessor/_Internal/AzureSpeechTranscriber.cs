using Azure.AI.Speech.Transcription;

namespace CFC.ChatAI.VideoProcessor._Internal;

internal sealed class AzureSpeechTranscriber : IAudioTranscriber
{
    private readonly TranscriptionClient _client;
    private readonly VideoProcessorOptions _options;

    public AzureSpeechTranscriber(TranscriptionClient client, VideoProcessorOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<(string Text, TimeSpan Duration)> TranscribeAsync(
        Stream audio,
        CancellationToken cancellationToken)
    {
        var request = new TranscriptionOptions(audio);
        foreach (var locale in _options.Locales)
        {
            request.Locales.Add(locale);
        }

        if (_options.EnableSpeakerDiarization)
        {
            request.DiarizationOptions = new TranscriptionDiarizationOptions
            {
                MaxSpeakers = _options.MaxSpeakers
            };
        }

        var response = await _client.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
        var result = response.Value;
        var phraseText = result.Phrases
            .OrderBy(phrase => phrase.Offset)
            .Select(phrase => phrase.Text?.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));
        var text = string.Join(' ', phraseText);

        if (text.Length == 0)
        {
            text = string.Join(' ', result.CombinedPhrases
                .Select(phrase => phrase.Text?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return (text, result.Duration);
    }
}

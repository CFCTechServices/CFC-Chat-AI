namespace CFC.ChatAI.VideoProcessor._Internal;

internal interface IAudioTranscriber
{
    Task<(string Text, TimeSpan Duration)> TranscribeAsync(Stream audio, CancellationToken cancellationToken);
}

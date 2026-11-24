namespace CFC.ChatAI.VideoProcessor._Internal;

internal interface IVideoAudioExtractor
{
    Task<string> ExtractAsync(
        Stream video,
        string sourceName,
        string workingDirectory,
        CancellationToken cancellationToken);
}

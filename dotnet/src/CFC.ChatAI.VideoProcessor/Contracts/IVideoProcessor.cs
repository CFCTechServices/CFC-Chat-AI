namespace CFC.ChatAI.VideoProcessor;

/// <summary>Extracts a video's audio, transcribes it, and returns bounded text chunks.</summary>
public interface IVideoProcessor
{
    Task<VideoProcessingResult> ProcessAsync(
        VideoInput video,
        CancellationToken cancellationToken = default);
}

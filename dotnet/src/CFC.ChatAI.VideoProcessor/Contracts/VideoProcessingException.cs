namespace CFC.ChatAI.VideoProcessor;

/// <summary>Raised when video extraction or transcription fails.</summary>
public sealed class VideoProcessingException : Exception
{
    public VideoProcessingException(string message) : base(message) { }

    public VideoProcessingException(string message, Exception innerException) : base(message, innerException) { }
}

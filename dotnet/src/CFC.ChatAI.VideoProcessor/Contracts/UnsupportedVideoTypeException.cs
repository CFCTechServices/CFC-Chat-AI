namespace CFC.ChatAI.VideoProcessor;

/// <summary>Raised when a video filename has an unsupported or missing extension.</summary>
public sealed class UnsupportedVideoTypeException : Exception
{
    public UnsupportedVideoTypeException(string extension)
        : base($"Video type '{extension}' is not supported.") => Extension = extension;

    public string Extension { get; }
}

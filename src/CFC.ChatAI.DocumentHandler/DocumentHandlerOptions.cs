namespace CFC.ChatAI.DocumentHandler;

/// <summary>Resource limits for coordinating document/video processing and persistence.</summary>
public sealed class DocumentHandlerOptions
{
    /// <summary>Maximum number of bytes staged for one upload. Individual processors may enforce lower limits.</summary>
    public long MaxUploadBytes { get; set; } = 500L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxUploadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUploadBytes), "The upload size limit must be positive.");
        }
    }
}

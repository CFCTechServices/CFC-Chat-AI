namespace CFC.ChatAI.VideoProcessor;

/// <summary>Transcript output from processing a video, independent of storage and indexing.</summary>
public sealed record VideoProcessingResult(
    string DocumentId,
    string SourceName,
    string SourceType,
    TimeSpan Duration,
    IReadOnlyList<VideoSection> Sections,
    IReadOnlyList<VideoChunk> Chunks);

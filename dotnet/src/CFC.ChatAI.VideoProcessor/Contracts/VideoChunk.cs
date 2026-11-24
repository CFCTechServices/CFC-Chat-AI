namespace CFC.ChatAI.VideoProcessor;

/// <summary>Extracted, searchable transcript text belonging to a video section.</summary>
public sealed record VideoChunk(string Id, string SectionId, string SectionTitle, string Text);

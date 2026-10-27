namespace CFC.ChatAI.VideoProcessor;

/// <summary>A named video and its content stream. The processor does not dispose the stream.</summary>
public sealed record VideoInput(string FileName, Stream Content);

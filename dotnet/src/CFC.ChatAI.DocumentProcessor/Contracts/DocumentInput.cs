namespace CFC.ChatAI.DocumentProcessor;

/// <summary>A named document and its content stream. The processor does not dispose the stream.</summary>
public sealed record DocumentInput(string FileName, Stream Content);

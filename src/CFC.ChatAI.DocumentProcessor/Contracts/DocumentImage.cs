namespace CFC.ChatAI.DocumentProcessor;

/// <summary>A supported embedded image, identified by its content within the document.</summary>
public sealed record DocumentImage(string Id, string Extension, string ContentType, ReadOnlyMemory<byte> Content);

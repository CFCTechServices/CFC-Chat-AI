namespace CFC.ChatAI.DocumentHandler;

/// <summary>A named uploaded file. The handler does not dispose the supplied stream.</summary>
public sealed record DocumentHandlerInput(string FileName, Stream Content, string? ContentType = null);

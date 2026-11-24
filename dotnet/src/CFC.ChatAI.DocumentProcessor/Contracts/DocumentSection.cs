namespace CFC.ChatAI.DocumentProcessor;

/// <summary>A logical heading or document body from which chunks were produced.</summary>
public sealed record DocumentSection(string Id, string Title, int Level);

namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Extracted, searchable text belonging to a document section.</summary>
public sealed record DocumentChunk(string Id, string SectionId, string SectionTitle, string Text);

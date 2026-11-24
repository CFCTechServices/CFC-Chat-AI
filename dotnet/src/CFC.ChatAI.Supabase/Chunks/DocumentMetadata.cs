namespace CFC.ChatAI.Supabase.Chunks;

/// <summary>Document-level source metadata shared by its persisted chunks.</summary>
public sealed record DocumentMetadata(string DocumentId, string? Source, string? SourceType);

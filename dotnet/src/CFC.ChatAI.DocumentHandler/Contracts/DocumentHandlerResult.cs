using CFC.ChatAI.Supabase.Storage;

namespace CFC.ChatAI.DocumentHandler;

/// <summary>Summary of a successfully processed, persisted, and indexed upload.</summary>
public sealed record DocumentHandlerResult(
    string DocumentId,
    string SourceName,
    string SourceType,
    int ChunkCount,
    int VectorCount,
    ObjectKey SourceObject);

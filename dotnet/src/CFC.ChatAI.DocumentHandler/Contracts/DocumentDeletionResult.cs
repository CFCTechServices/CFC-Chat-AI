namespace CFC.ChatAI.DocumentHandler;

/// <summary>Summary of a deletion workflow; the participating providers do not share a transaction.</summary>
public sealed record DocumentDeletionResult(
    string DocumentId,
    bool Found,
    int ChunkCount,
    int StorageObjectCount);

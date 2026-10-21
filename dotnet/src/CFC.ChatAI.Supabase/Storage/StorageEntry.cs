namespace CFC.ChatAI.Supabase.Storage;

/// <summary>A direct child returned by a storage listing; folders must be traversed separately.</summary>
public sealed record StorageEntry(
    ObjectKey Key,
    bool IsFolder,
    string? ContentType = null,
    long? Size = null);

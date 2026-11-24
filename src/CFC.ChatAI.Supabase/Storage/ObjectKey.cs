namespace CFC.ChatAI.Supabase.Storage;

/// <summary>Stable logical key for one stored artifact.</summary>
public sealed record ObjectKey(StorageArea Area, string Path);

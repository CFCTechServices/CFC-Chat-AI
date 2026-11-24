namespace CFC.ChatAI.Supabase.Storage;

/// <summary>Prefix for a paginated object listing.</summary>
public sealed record ObjectPrefix(StorageArea Area, string Path);

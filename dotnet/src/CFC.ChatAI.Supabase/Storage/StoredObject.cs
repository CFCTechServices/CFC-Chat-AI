namespace CFC.ChatAI.Supabase.Storage;

public sealed record StoredObject(
    ObjectKey Key,
    string? ContentType = null,
    long? Size = null,
    Uri? PublicUri = null);

namespace CFC.ChatAI.Supabase.Storage;

public sealed record UploadOptions(string? ContentType = null, bool Overwrite = false);

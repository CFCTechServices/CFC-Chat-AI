using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Storage;

/// <summary>The storage provider rejected a create because the object already exists.</summary>
public sealed class StorageObjectConflictException()
    : DependencyFailureException("Supabase Storage", "create without overwrite");

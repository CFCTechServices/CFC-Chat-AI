using CFC.ChatAI.Supabase.Common;

namespace CFC.ChatAI.Supabase.Conversations;

/// <summary>Messages were deleted, but the subsequent session delete failed.</summary>
public sealed class SessionDeletionPartialFailureException()
    : DependencyFailureException("Supabase", "session deletion after message deletion");

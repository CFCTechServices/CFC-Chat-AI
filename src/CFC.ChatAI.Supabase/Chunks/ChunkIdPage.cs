namespace CFC.ChatAI.Supabase.Chunks;

public sealed record ChunkIdPage(IReadOnlyList<string> ChunkIds, bool HasMore);

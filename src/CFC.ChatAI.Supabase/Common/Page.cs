namespace CFC.ChatAI.Supabase.Common;

/// <summary>A page of results with an explicit continuation indicator.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore);

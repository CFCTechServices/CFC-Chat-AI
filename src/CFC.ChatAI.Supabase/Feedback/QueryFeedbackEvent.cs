namespace CFC.ChatAI.Supabase.Feedback;

/// <summary>One query/rating signal associated with a chunk cited by a message.</summary>
public sealed record QueryFeedbackEvent(string ChunkId, string MessageId, int Rating);

namespace CFC.ChatAI.Supabase.Feedback;

/// <summary>A vote change submitted for an assistant message. Null rating clears the vote.</summary>
public sealed record FeedbackSubmission(string MessageId, string UserId, int? Rating);

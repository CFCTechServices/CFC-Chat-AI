namespace CFC.ChatAI.Supabase.Feedback;

/// <summary>A batch of events sharing one query embedding.</summary>
public sealed record QueryFeedbackEventSet(
    IReadOnlyList<QueryFeedbackEvent> Events,
    IReadOnlyList<float> QueryEmbedding);

namespace CFC.ChatAI.Feedback.Contracts;

/// <summary>The ordered, feedback-reranked results for one semantic search.</summary>
public sealed record FeedbackSearchResult(IReadOnlyList<FeedbackSearchMatch> Matches);

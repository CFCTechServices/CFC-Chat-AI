using System.Text.Json;

namespace CFC.ChatAI.Feedback.Contracts;

/// <summary>Searches Pinecone and reranks its matches using Supabase feedback signals.</summary>
public interface IFeedbackService
{
    /// <summary>
    /// Searches for relevant chunks, hydrates their content from Supabase, and applies feedback
    /// reranking. Feedback reads and chunk hydration fail open; vector search failures propagate.
    /// </summary>
    Task<FeedbackSearchResult> SearchAsync(
        string query,
        int topK = 5,
        IReadOnlyDictionary<string, JsonElement>? filter = null,
        CancellationToken cancellationToken = default);
}

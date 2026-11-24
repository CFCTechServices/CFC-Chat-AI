using CFC.ChatAI.Supabase.Conversations;

namespace CFC.ChatAI.ChatService.Internal;

/// <summary>Internal seam for model completion; the public component contract does not expose Azure SDK types.</summary>
internal interface IChatCompletionClient
{
    Task<string> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        string userPrompt,
        float temperature,
        int maxOutputTokens,
        CancellationToken cancellationToken);
}

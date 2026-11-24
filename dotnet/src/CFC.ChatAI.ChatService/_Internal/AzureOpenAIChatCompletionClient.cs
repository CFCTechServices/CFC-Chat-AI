using System.ClientModel;
using System.Text;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using StoredChatMessage = CFC.ChatAI.Supabase.Conversations.ChatMessage;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace CFC.ChatAI.ChatService.Internal;

/// <summary>Azure OpenAI adapter using the supported Azure.AI.OpenAI chat-completions client.</summary>
internal sealed class AzureOpenAIChatCompletionClient : IChatCompletionClient
{
    private readonly ChatClient _chatClient;

    public AzureOpenAIChatCompletionClient(ChatServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clientOptions = new AzureOpenAIClientOptions
        {
            NetworkTimeout = options.AzureOpenAIRequestTimeout,
        };
        var azureClient = new AzureOpenAIClient(
            new Uri(options.AzureOpenAIEndpoint),
            new ApiKeyCredential(options.AzureOpenAIApiKey),
            clientOptions);
        _chatClient = azureClient.GetChatClient(options.AzureOpenAIDeploymentName);
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<StoredChatMessage> history,
        string userPrompt,
        float temperature,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new List<SdkChatMessage>(history.Count + 2)
        {
            new SystemChatMessage(systemPrompt),
        };
        foreach (var prior in history)
        {
            if (string.Equals(prior.Role, "user", StringComparison.Ordinal))
                messages.Add(new UserChatMessage(prior.Content));
            else if (string.Equals(prior.Role, "assistant", StringComparison.Ordinal))
                messages.Add(new AssistantChatMessage(prior.Content));
        }
        messages.Add(new UserChatMessage(userPrompt));

        var completion = await _chatClient.CompleteChatAsync(
            messages,
            new ChatCompletionOptions
            {
                Temperature = temperature,
                MaxOutputTokenCount = maxOutputTokens,
            },
            cancellationToken).ConfigureAwait(false);

        var text = new StringBuilder();
        foreach (var part in completion.Value.Content)
        {
            if (!string.IsNullOrEmpty(part.Text))
                text.Append(part.Text);
        }

        if (text.Length == 0)
            throw new InvalidOperationException("Azure OpenAI returned an empty chat completion.");

        return text.ToString();
    }
}

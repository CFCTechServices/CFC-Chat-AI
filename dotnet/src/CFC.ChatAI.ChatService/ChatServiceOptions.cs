namespace CFC.ChatAI.ChatService;

/// <summary>Azure OpenAI and request limits for chat orchestration.</summary>
public sealed class ChatServiceOptions
{
    public const string SectionName = "ChatService";

    /// <summary>Azure OpenAI resource endpoint. Leave all Azure OpenAI fields empty to use extractive fallback answers.</summary>
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;

    /// <summary>Azure OpenAI API key. Prefer loading this value from a secret store.</summary>
    public string AzureOpenAIApiKey { get; set; } = string.Empty;

    /// <summary>The Azure OpenAI model deployment name, not the base model name.</summary>
    public string AzureOpenAIDeploymentName { get; set; } = string.Empty;

    /// <summary>Timeout for one Azure OpenAI network operation. Defaults to 30 seconds.</summary>
    public TimeSpan AzureOpenAIRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of reranked chunks to retrieve per message. Defaults to five.</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Maximum persisted messages loaded for model context, including the current user message.</summary>
    public int MaxHistoryMessages { get; set; } = 10;

    /// <summary>Maximum number of characters of retrieved chunk context sent to Azure OpenAI.</summary>
    public int MaxContextLength { get; set; } = 4_000;

    /// <summary>Maximum number of output tokens requested from Azure OpenAI.</summary>
    public int MaxOutputTokens { get; set; } = 500;

    /// <summary>Chat completion temperature. Defaults to the low-variance Python implementation value.</summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>Maximum number of candidate document images to offer to the model.</summary>
    public int MaxImages { get; set; } = 3;

    /// <summary>Minimum feedback-adjusted relevance score for an image candidate.</summary>
    public double MinimumImageScore { get; set; } = 0.3;

    internal bool IsAzureOpenAIConfigured =>
        !string.IsNullOrWhiteSpace(AzureOpenAIEndpoint) &&
        !string.IsNullOrWhiteSpace(AzureOpenAIApiKey) &&
        !string.IsNullOrWhiteSpace(AzureOpenAIDeploymentName);

    internal void Validate()
    {
        var hasEndpoint = !string.IsNullOrWhiteSpace(AzureOpenAIEndpoint);
        var hasApiKey = !string.IsNullOrWhiteSpace(AzureOpenAIApiKey);
        var hasDeployment = !string.IsNullOrWhiteSpace(AzureOpenAIDeploymentName);
        if (hasEndpoint || hasApiKey || hasDeployment)
        {
            if (!(hasEndpoint && hasApiKey && hasDeployment))
                throw new InvalidOperationException(
                    "AzureOpenAIEndpoint, AzureOpenAIApiKey, and AzureOpenAIDeploymentName must be configured together.");

            if (!Uri.TryCreate(AzureOpenAIEndpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("https" or "http") ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new InvalidOperationException(
                    "AzureOpenAIEndpoint must be an absolute HTTP(S) URL without embedded credentials, query, or fragment.");
            }
        }

        if (AzureOpenAIRequestTimeout <= TimeSpan.Zero || AzureOpenAIRequestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(AzureOpenAIRequestTimeout), "Azure OpenAI request timeout must be positive and no more than five minutes.");
        if (DefaultTopK is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(DefaultTopK), "DefaultTopK must be between 1 and 100.");
        if (MaxHistoryMessages is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(MaxHistoryMessages), "MaxHistoryMessages must be between 1 and 100.");
        if (MaxContextLength is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(MaxContextLength), "MaxContextLength must be between 1 and 1,000,000 characters.");
        if (MaxOutputTokens is < 1 or > 32_768)
            throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "MaxOutputTokens must be between 1 and 32,768.");
        if (!float.IsFinite(Temperature) || Temperature is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(Temperature), "Temperature must be finite and between 0 and 2.");
        if (MaxImages is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(MaxImages), "MaxImages must be between 0 and 20.");
        if (!double.IsFinite(MinimumImageScore) || MinimumImageScore is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumImageScore), "MinimumImageScore must be finite and between -1 and 1.");
    }
}

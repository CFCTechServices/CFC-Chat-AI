using System.Text.Json;
using CFC.ChatAI.ChatService;
using CFC.ChatAI.ChatService.Contracts;
using CFC.ChatAI.ChatService.Internal;
using CFC.ChatAI.Feedback.Contracts;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Conversations;
using Microsoft.Extensions.DependencyInjection;
using StoredChatMessage = CFC.ChatAI.Supabase.Conversations.ChatMessage;

namespace CFC.ChatAI.ChatService.Tests;

public sealed class ChatServiceTests
{
    [Fact]
    public async Task SendMessage_retrieves_generates_and_persists_snake_case_metadata()
    {
        const string imagePath = "docs/doc-1/images/chart.png";
        const string otherChunkImagePath = "docs/doc-1/images/other.png";
        const string rawAnswer = "The guide explains the nutrients. [IMAGE: docs/doc-1/images/chart.png][IMAGE: docs/doc-1/images/other.png][IMAGE: docs/doc-1/images/not-retrieved.png][CHUNKS_CITED: chunk-1]";
        var repository = new FakeConversationRepository(
            ExistingMessage("history-user", "user", "What does the guide cover?"),
            ExistingMessage("history-assistant", "assistant", "It covers feed nutrients."));
        var feedback = new FakeFeedbackService(new FeedbackSearchResult(
        [
            Match(1, "chunk-1", 0.91, new Chunk(
                "chunk-1", "doc-1", "Vitamin and mineral details for the feed.",
                SectionId: "section-1", SectionTitle: "Nutrient guide", ImagePaths: [imagePath],
                Source: "guide.pdf", SourceType: "document")),
            Match(2, "chunk-2", 0.4, new Chunk(
                "chunk-2", "doc-1", "A less relevant paragraph.",
                ImagePaths: [otherChunkImagePath], Source: "guide.pdf")),
        ]));
        var completion = new FakeChatCompletionClient(rawAnswer);
        using var provider = CreateProvider(repository, feedback, completion);
        var service = provider.GetRequiredService<IChatService>();

        var result = await service.SendMessageAsync(
            "user-1", "caller-access-token", "session-1", "Which nutrients are discussed?");

        Assert.Equal("assistant", result.Role);
        Assert.Equal("The guide explains the nutrients.", result.Content);
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal("chunk-1", result.Citations[0].ChunkId);
        Assert.Equal("Nutrient guide", result.Citations[0].SectionTitle);
        Assert.Equal("doc-1", result.Citations[0].DocId);
        Assert.Equal(0.91, result.Citations[0].Score, precision: 6);
        var image = Assert.Single(result.RelevantImages);
        Assert.Equal(imagePath, image.Path);
        Assert.Equal(rawAnswer.IndexOf("[IMAGE:", StringComparison.Ordinal), image.Position);
        Assert.Equal("Nutrient guide", image.AltText);

        Assert.Equal("Which nutrients are discussed?", feedback.LastQuery);
        Assert.Equal(5, feedback.LastTopK);
        Assert.Equal("user-1", repository.LastUserId);
        Assert.Equal("caller-access-token", repository.LastAccessToken);
        Assert.Equal(new[] { "history-user", "history-assistant" }, completion.History!.Select(message => message.Id));
        Assert.DoesNotContain(completion.History!, message => message.Content == "Which nutrients are discussed?");
        Assert.Contains("[CHUNKS_CITED:", completion.SystemPrompt);
        Assert.Contains("[CHUNK_ID: chunk-1]", completion.UserPrompt);
        Assert.Contains($"[IMAGE: {imagePath}]", completion.UserPrompt);

        Assert.Collection(
            repository.InsertedMessages,
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal("Which nutrients are discussed?", user.Content);
                Assert.Null(user.Metadata);
            },
            assistant =>
            {
                Assert.Equal("assistant", assistant.Role);
                Assert.Equal(result.Content, assistant.Content);
                Assert.NotNull(assistant.Metadata);
                var storedCitations = assistant.Metadata!.Value.GetProperty("citations");
                Assert.Equal("chunk-1", storedCitations[0].GetProperty("chunk_id").GetString());
                Assert.Equal(imagePath,
                    assistant.Metadata.Value.GetProperty("relevant_images")[0].GetProperty("path").GetString());
            });
    }

    [Fact]
    public async Task SendMessage_throws_not_found_before_search_when_session_is_missing()
    {
        var repository = new FakeConversationRepository(sessionExists: false);
        var feedback = new FakeFeedbackService(new FeedbackSearchResult([]));
        var completion = new FakeChatCompletionClient("unused");
        using var provider = CreateProvider(repository, feedback, completion);
        var service = provider.GetRequiredService<IChatService>();

        await Assert.ThrowsAsync<ChatSessionNotFoundException>(() => service.SendMessageAsync(
            "user-1", "caller-access-token", "missing-session", "Hello"));

        Assert.Single(repository.InsertedMessages);
        Assert.Equal(0, feedback.CallCount);
        Assert.Equal(0, completion.CallCount);
    }

    [Fact]
    public async Task SendMessage_falls_back_to_context_summary_when_model_call_fails()
    {
        var repository = new FakeConversationRepository();
        var feedback = new FakeFeedbackService(new FeedbackSearchResult(
        [
            Match(1, "chunk-1", 0.8, new Chunk(
                "chunk-1", "doc-1", "The feed contains vitamins and minerals.",
                SectionTitle: "Feed composition", ImagePaths: ["docs/doc-1/images/feed.png"],
                Source: "feed.pdf")),
        ]));
        var completion = new FakeChatCompletionClient("unused")
        {
            Error = new HttpRequestException("Azure OpenAI is unavailable."),
        };
        using var provider = CreateProvider(repository, feedback, completion);
        var service = provider.GetRequiredService<IChatService>();

        var result = await service.SendMessageAsync(
            "user-1", "caller-access-token", "session-1", "Summarize the feed composition.");

        Assert.Equal("Feed composition: The feed contains vitamins and minerals.", result.Content);
        Assert.Null(Assert.Single(result.RelevantImages).Position);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal("assistant", Assert.Single(repository.InsertedMessages, message => message.Role == "assistant").Role);
    }

    [Fact]
    public async Task SendMessage_without_model_or_matches_returns_no_context_fallback()
    {
        var repository = new FakeConversationRepository();
        var feedback = new FakeFeedbackService(new FeedbackSearchResult([]));
        using var provider = CreateProvider(repository, feedback);
        var service = provider.GetRequiredService<IChatService>();

        var result = await service.SendMessageAsync(
            "user-1", "caller-access-token", "session-1", "Hello there");

        Assert.Equal("I couldn't find relevant information in the uploaded content for that question.", result.Content);
        Assert.Empty(result.Citations);
        Assert.Empty(result.RelevantImages);
        Assert.Equal("assistant", repository.InsertedMessages[1].Role);
    }

    [Fact]
    public async Task SendMessage_propagates_retrieval_failure_after_saving_user_message()
    {
        var repository = new FakeConversationRepository();
        var feedback = new FakeFeedbackService(new FeedbackSearchResult([]))
        {
            Error = new HttpRequestException("Vector search failed."),
        };
        using var provider = CreateProvider(repository, feedback);
        var service = provider.GetRequiredService<IChatService>();

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendMessageAsync(
            "user-1", "caller-access-token", "session-1", "Question"));

        Assert.Single(repository.InsertedMessages);
        Assert.Equal("user", repository.InsertedMessages[0].Role);
    }

    [Fact]
    public void AddChatService_rejects_partial_azure_openai_configuration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddChatService(options =>
        {
            options.AzureOpenAIEndpoint = "https://example.openai.azure.com/";
        }));
    }

    private static ServiceProvider CreateProvider(
        FakeConversationRepository repository,
        FakeFeedbackService feedback,
        FakeChatCompletionClient? completion = null,
        Action<ChatServiceOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConversationRepository>(repository);
        services.AddSingleton<IFeedbackService>(feedback);
        if (completion is not null)
            services.AddSingleton<IChatCompletionClient>(completion);
        services.AddChatService(configure);
        return services.BuildServiceProvider();
    }

    private static FeedbackSearchMatch Match(int rank, string id, double score, Chunk chunk) =>
        new(rank, score, new PineconeSearchMatch(id, (float)score, null), chunk);

    private static StoredChatMessage ExistingMessage(string id, string role, string content) =>
        new(id, "session-1", role, content, DateTimeOffset.UtcNow);

    private sealed class FakeChatCompletionClient(string response) : IChatCompletionClient
    {
        public int CallCount { get; private set; }
        public Exception? Error { get; init; }
        public string? SystemPrompt { get; private set; }
        public IReadOnlyList<StoredChatMessage>? History { get; private set; }
        public string? UserPrompt { get; private set; }

        public Task<string> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<StoredChatMessage> history,
            string userPrompt,
            float temperature,
            int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            SystemPrompt = systemPrompt;
            History = history;
            UserPrompt = userPrompt;
            if (Error is not null)
                throw Error;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeFeedbackService(FeedbackSearchResult result) : IFeedbackService
    {
        public int CallCount { get; private set; }
        public Exception? Error { get; init; }
        public string? LastQuery { get; private set; }
        public int LastTopK { get; private set; }

        public Task<FeedbackSearchResult> SearchAsync(
            string query,
            int topK = 5,
            IReadOnlyDictionary<string, JsonElement>? filter = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastQuery = query;
            LastTopK = topK;
            if (Error is not null)
                throw Error;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        private readonly string _sessionId;
        private readonly bool _sessionExists;
        private readonly List<StoredChatMessage> _messages;
        private int _nextId;

        public FakeConversationRepository(
            params StoredChatMessage[] history)
            : this(sessionExists: true, "session-1", history)
        {
        }

        public FakeConversationRepository(
            bool sessionExists,
            string sessionId = "session-1",
            params StoredChatMessage[] history)
        {
            _sessionExists = sessionExists;
            _sessionId = sessionId;
            _messages = history.ToList();
        }

        public List<CreateChatMessage> InsertedMessages { get; } = [];
        public string? LastUserId { get; private set; }
        public string? LastAccessToken { get; private set; }

        public Task<Page<ChatSession>> ListSessionsAsync(
            string userId,
            string accessToken,
            PageRequest page,
            bool includeDetails = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChatSession?> CreateSessionAsync(
            string userId,
            string accessToken,
            string? title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChatSession?> GetSessionAsync(
            string userId,
            string accessToken,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatSession?>(_sessionExists && sessionId == _sessionId
                ? new ChatSession(_sessionId, userId, null, DateTimeOffset.UtcNow)
                : null);

        public Task<ChatSession?> UpdateSessionTitleAsync(
            string userId,
            string accessToken,
            string sessionId,
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteSessionAsync(
            string userId,
            string accessToken,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredChatMessage>> GetHistoryAsync(
            string userId,
            string accessToken,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredChatMessage>> GetLastMessagesAsync(
            string userId,
            string accessToken,
            string sessionId,
            int count,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastUserId = userId;
            LastAccessToken = accessToken;
            IReadOnlyList<StoredChatMessage> latest = _messages.TakeLast(count).ToArray();
            return Task.FromResult(latest);
        }

        public Task<StoredChatMessage?> InsertMessageAsync(
            string userId,
            string accessToken,
            CreateChatMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastUserId = userId;
            LastAccessToken = accessToken;
            InsertedMessages.Add(message);
            if (!_sessionExists || message.SessionId != _sessionId)
                return Task.FromResult<StoredChatMessage?>(null);

            var stored = new StoredChatMessage(
                $"message-{++_nextId}",
                message.SessionId,
                message.Role,
                message.Content,
                DateTimeOffset.UtcNow,
                message.Metadata is { } metadata ? metadata.Clone() : null);
            _messages.Add(stored);
            return Task.FromResult<StoredChatMessage?>(stored);
        }

        public Task<FeedbackMessageContext?> GetFeedbackMessageContextAsync(
            string userId,
            string accessToken,
            string messageId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

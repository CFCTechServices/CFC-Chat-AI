using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed class ChatWorkspaceTests : IDisposable
{
    private readonly ApiHandler handler = new();
    private readonly TestAccess access = new("account");
    private readonly HttpClient http;
    private readonly ChatWorkspace chat;

    public ChatWorkspaceTests()
    {
        http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        chat = NewPage();
    }

    private ChatWorkspace NewPage() => new(new FastApiClient(http), access, access.Owner);

    [Fact]
    public async Task LoadsRememberedSessionAndRestoresSavedMessagesOnRefresh()
    {
        for (var opening = 0; opening < 2; opening++)
        {
            using var page = NewPage();
            handler.Expect("GET", "sessions", new[] { Session("newest"), Session("chosen"), Session("foreign", owner: "other") });
            handler.Expect("GET", "sessions/chosen", new[] { Row("chosen", "m1", "user", "Question"), Row("chosen", "m2", "assistant", "Answer") });
            await page.LoadAsync("chosen");
            Assert.Equal(2, page.Conversations.Count);
            var thread = page.Active!;
            Assert.Equal("chosen", thread.Id);
            Assert.Equal(new[] { "Question", "Answer" }, thread.Messages.Select(m => m.Content));
            var reply = thread.Messages[1];
            Assert.Equal("m2", reply.Id);
            Assert.Equal("citation", reply.Metadata!.Citations![0]["source"].GetString());
            Assert.Equal("docs/example.png", reply.Metadata.RelevantImages![0].Path);
            Assert.True(page.CanSend);
        }
    }

    [Fact]
    public async Task FirstSendCreatesSessionOnceAndDisplaysRealReplyWhileAnotherThreadIsSelected()
    {
        handler.Expect("GET", "sessions", Array.Empty<object>());
        await chat.LoadAsync(null);
        var creation = handler.Hold("POST", "sessions", new { title = "New Chat" });
        var answer = handler.Hold("POST", "message", new { session_id = "first", content = "Question" });
        var sending = chat.SendAsync(" Question ");
        await creation.Started.Task;
        await chat.SendAsync("Question");
        Assert.False(chat.CanSend);
        creation.Return(Session("first"));
        await answer.Started.Task;
        var first = chat.Active!;
        Assert.Equal("Question", first.PendingContent);
        await chat.SendAsync("Question");
        await chat.DeleteAsync(first);

        handler.Expect("POST", "sessions", Session("second"), new { title = "New Chat" });
        var second = await chat.NewAsync();
        Assert.Same(second, chat.Active);
        Assert.True(chat.CanSend);
        handler.Expect("PATCH", "sessions/first", Session("first", "Question"), new { title = "Question" });
        answer.Return(Reply());
        await sending;
        Assert.Empty(second!.Messages);
        Assert.Same(second, chat.Active);
        Assert.Equal("Question", first.Title);
        Assert.Equal("Question", first.Messages[0].Content);
        var assistant = first.Messages[1];
        Assert.Equal("m2", assistant.Id);
        Assert.Equal("Answer from API", assistant.Content);
        Assert.Equal("citation", assistant.Metadata!.Citations![0]["source"].GetString());
        Assert.Equal("docs/example.png", assistant.Metadata.RelevantImages![0].Path);
        Assert.Null(first.PendingContent);
        Assert.Equal(1, handler.MessagePosts);
    }

    [Fact]
    public async Task SlowHistoryUpdatesOnlyTheThreadThatRequestedIt()
    {
        handler.Expect("GET", "sessions", new[] { Session("a"), Session("b") });
        var slow = handler.Hold("GET", "sessions/a");
        var loading = chat.LoadAsync(null);
        await slow.Started.Task;
        var first = chat.Active!;
        handler.Expect("GET", "sessions/b", new[] { Row("b", "b1", "user", "Second") });
        await chat.SelectAsync(chat.Conversations[1]);
        slow.Return(new[] { Row("a", "a1", "user", "First") });
        await loading;
        Assert.Equal("Second", Assert.Single(chat.Active!.Messages).Content);
        Assert.Equal("First", Assert.Single(first.Messages).Content);
    }

    [Fact]
    public async Task ConcurrentAccountsUseSeparateBearersAndKeepTheirOwnReplies()
    {
        await OpenAsync(Row("a", "a0", "user", "Earlier"));
        var secondAccess = new TestAccess("other");
        using var other = new ChatWorkspace(new FastApiClient(http), secondAccess, secondAccess.Owner);
        handler.Expect("GET", "sessions", new[] { Session("b", owner: "other") });
        handler.Expect("GET", "sessions/b", new[] { Row("b", "b0", "user", "Earlier") });
        await other.LoadAsync(null);
        var first = handler.Hold("POST", "message", new { session_id = "a", content = "First" });
        var second = handler.Hold("POST", "message", new { session_id = "b", content = "Second" });
        var sendingFirst = chat.SendAsync("First");
        var sendingSecond = other.SendAsync("Second");
        await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(access.Token, first.Bearer);
        Assert.Equal(secondAccess.Token, second.Bearer);
        Assert.NotEqual(first.Bearer, second.Bearer);
        second.Return(Reply("Second answer"));
        await sendingSecond;
        Assert.Equal("First", chat.Active!.PendingContent);
        first.Return(Reply("First answer"));
        await sendingFirst;
        Assert.Equal("First answer", chat.Active.Messages.Last().Content);
        Assert.Equal("Second answer", other.Active!.Messages.Last().Content);
    }

    [Fact]
    public async Task DeleteWaitsForBackendThenLoadsTheNextConversation()
    {
        handler.Expect("GET", "sessions", new[] { Session("a"), Session("b") });
        handler.Expect("GET", "sessions/a", Array.Empty<object>());
        await chat.LoadAsync(null);
        var deletion = handler.Hold("DELETE", "sessions/a");
        var deleting = chat.DeleteAsync(chat.Active!);
        await deletion.Started.Task;
        Assert.Equal("a", chat.Active!.Id);
        handler.Expect("GET", "sessions/b", new[] { Row("b", "b1", "user", "Kept") });
        deletion.Return(new { success = true });
        await deleting;
        Assert.Equal("b", Assert.Single(chat.Conversations).Id);
        Assert.Equal("Kept", Assert.Single(chat.Active.Messages).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSendReconcilesPartialOrCompleteStoredTurn(bool savedAnswer)
    {
        await OpenAsync();
        handler.Fail("POST", "message", HttpStatusCode.InternalServerError);
        var persisted = new List<object> { Row("a", "m1", "user", "Question") };
        if (savedAnswer) persisted.Add(Row("a", "m2", "assistant", "Recovered answer"));
        handler.Expect("GET", "sessions/a", persisted);
        handler.Expect("PATCH", "sessions/a", Session("a", "Question"), new { title = "Question" });
        await chat.SendAsync("Question");
        Assert.Equal(savedAnswer ? 2 : 1, chat.Active!.Messages.Count);
        Assert.Equal(savedAnswer ? "Recovered answer" : "Question", chat.Active.Messages.Last().Content);
        Assert.Contains(savedAnswer ? "recovered" : "was saved", chat.Active.Notice);
        Assert.Empty(chat.Draft);
        Assert.Equal(1, handler.MessagePosts);
    }

    [Fact]
    public async Task RetryChecksHistoryAgainAndSuppressesPostIfOriginalArrivedLate()
    {
        var old = Row("a", "old", "user", "Repeated text");
        await OpenAsync(old);
        handler.Fail("POST", "message", HttpStatusCode.BadGateway);
        handler.Expect("GET", "sessions/a", new[] { old });
        await chat.SendAsync("Repeated text");
        Assert.Equal("Repeated text", chat.Draft);
        handler.Expect("GET", "sessions/a", new[] { old, Row("a", "new", "user", "Repeated text") });
        await chat.SendAsync(chat.Draft);
        Assert.Equal(1, handler.MessagePosts);
        Assert.Equal("new", chat.Active!.Messages.Last().Id);
        Assert.Empty(chat.Draft);
    }

    [Fact]
    public async Task FailedHistoryBlocksPostingUntilReloadAndExplicitRetry()
    {
        await OpenAsync(Row("a", "previous", "assistant", "Earlier answer"));
        handler.Fail("POST", "message", HttpStatusCode.ServiceUnavailable);
        handler.Fail("GET", "sessions/a", HttpStatusCode.ServiceUnavailable);
        await chat.SendAsync("Try again");
        Assert.False(chat.CanSend);
        await chat.SendAsync("Try again");
        Assert.Equal(1, handler.MessagePosts);
        var old = new[] { Row("a", "previous", "assistant", "Earlier answer") };
        handler.Expect("GET", "sessions/a", old);
        await chat.SelectAsync(chat.Active!);
        Assert.True(chat.CanSend);
        handler.Expect("GET", "sessions/a", old);
        handler.Expect("POST", "message", Reply(), new { session_id = "a", content = "Try again" });
        await chat.SendAsync(chat.Draft);
        Assert.Equal(2, handler.MessagePosts);
        Assert.Equal("Answer from API", chat.Active!.Messages.Last().Content);
    }

    [Fact]
    public async Task FailedTitleSaveCanBeRetriedWithoutSendingAnotherMessage()
    {
        await OpenAsync();
        handler.Expect("POST", "message", Reply(), new { session_id = "a", content = "Question" });
        handler.Fail("PATCH", "sessions/a", HttpStatusCode.ServiceUnavailable);
        await chat.SendAsync("Question");
        var thread = chat.Active!;
        Assert.Equal("New Chat", thread.Title);
        Assert.Equal("Question", thread.PendingTitle);
        Assert.Equal("Answer from API", thread.Messages.Last().Content);
        handler.Expect("PATCH", "sessions/a", Session("a", "Question"), new { title = "Question" });
        await chat.RetryTitleAsync(thread);
        Assert.Equal("Question", thread.Title);
        Assert.Null(thread.TitleError);
        Assert.Equal(1, handler.MessagePosts);
    }

    [Fact]
    public async Task ListAndDeleteFailuresRetainRecoverableState()
    {
        handler.Fail("GET", "sessions", HttpStatusCode.Unauthorized);
        await chat.LoadAsync(null);
        Assert.False(chat.CanSend);
        Assert.Contains("Access was denied", chat.Notice);
        await OpenAsync();
        handler.Fail("DELETE", "sessions/a", HttpStatusCode.BadGateway);
        await chat.DeleteAsync(chat.Active!);
        Assert.Single(chat.Conversations);
        Assert.False(chat.ListReady);
        handler.Expect("GET", "sessions", Array.Empty<object>());
        await chat.LoadAsync("a");
        Assert.Null(chat.Active);
    }

    [Fact]
    public async Task AmbiguousCreateRetainsDraftAndRequiresListingBeforeAnotherCreate()
    {
        handler.Expect("GET", "sessions", Array.Empty<object>());
        await chat.LoadAsync(null);
        chat.Draft = "Still unsent";
        handler.Fail("POST", "sessions", HttpStatusCode.GatewayTimeout);
        await chat.SendAsync(chat.Draft);
        await chat.SendAsync(chat.Draft);
        Assert.False(chat.CanSend);
        Assert.Equal(0, handler.MessagePosts);
        await OpenAsync();
        Assert.Equal("Still unsent", chat.Draft);
        Assert.Equal("a", chat.Active!.Id);
    }

    [Fact]
    public async Task AssistantReplyRendersInTheExistingMessageBubble()
    {
        await OpenAsync();
        handler.Expect("POST", "message", Reply(), new { session_id = "a", content = "Question" });
        handler.Expect("PATCH", "sessions/a", Session("a", "Question"), new { title = "Question" });
        await chat.SendAsync("Question");
        var answer = Assert.Single(chat.Active!.Messages, message => message.Role == "assistant");

        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoBrowser>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ChatMessage.Sender)] = "CFC AI",
                [nameof(ChatMessage.Text)] = answer.Content
            });
            var rendered = await renderer.RenderComponentAsync<ChatMessage>(parameters);
            return rendered.ToHtmlString();
        });
        Assert.Contains("chat-message bot", html);
        Assert.Contains("<p>Answer from API</p>", html);
        Assert.Contains("Copy message", html);
    }

    private async Task OpenAsync(params object[] history)
    {
        handler.Expect("GET", "sessions", new[] { Session("a") });
        handler.Expect("GET", "sessions/a", history);
        await chat.LoadAsync("a");
    }

    private static object Session(string id, string title = "New Chat", string owner = "account") =>
        new { id, title, user_id = owner, created_at = "2026-01-01" };
    private static object Row(string session, string id, string role, string content) =>
        new { session_id = session, id, role, content, created_at = "2026-01-01", metadata = Media() };
    private static object Media() => new
    {
        citations = new[] { new { source = "citation" } },
        relevant_images = new[] { new { path = "docs/example.png" } }
    };
    private static object Reply(string content = "Answer from API") => new
    {
        id = "m2", role = "assistant", content, created_at = "2026-01-01",
        citations = new[] { new { source = "citation" } }, relevant_images = new[] { new { path = "docs/example.png" } }
    };

    public void Dispose()
    {
        chat.Dispose();
        http.Dispose();
        Assert.Empty(handler.Queue);
    }

    private sealed class TestAccess(string owner) : IChatSessionAccess
    {
        public string Owner => owner;
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public Task<T> WithAccessTokenAsync<T>(string ownerId, Func<string, CancellationToken, Task<T>> request, CancellationToken ct)
        {
            Assert.Equal(owner, ownerId);
            return request(Token, ct);
        }
    }

    private sealed class Pending
    {
        public string? Bearer { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Return(object payload) => Response.SetResult(new() { Content = JsonContent.Create(payload) });
    }

    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => throw new NotSupportedException();
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => throw new NotSupportedException();
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        public Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Queue { get; } = [];
        public int MessagePosts { get; private set; }
        public Pending Hold(string method, string path, object? body = null)
        {
            var pending = new Pending();
            Queue.Enqueue(async (req, ct) =>
            {
                Assert.Equal(method, req.Method.Method);
                Assert.Equal("/api/chat/" + path, req.RequestUri!.PathAndQuery);
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                pending.Bearer = req.Headers.Authorization!.Parameter;
                if (body is not null)
                {
                    var json = await req.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(body), json));
                }
                pending.Started.SetResult();
                return await pending.Response.Task.WaitAsync(ct);
            });
            return pending;
        }
        public void Expect(string method, string path, object result, object? body = null) =>
            Hold(method, path, body).Return(result);
        public void Fail(string method, string path, HttpStatusCode code) =>
            Hold(method, path).Response.SetResult(new(code) { Content = JsonContent.Create(new { detail = "Failed" }) });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/chat/message") MessagePosts++;
            return Queue.Dequeue()(request, ct);
        }
    }
}

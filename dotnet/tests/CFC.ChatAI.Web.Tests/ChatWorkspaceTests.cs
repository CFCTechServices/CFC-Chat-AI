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

    private sealed class PreviewBrowser : IJSRuntime
    {
        public List<string> Released { get; } = [];
        public LocalImage Image { get; set; } = new("blob:https://example.test/first", "sample.png", 100);
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            if (name == "cfcChat.selectImages")
                return ValueTask.FromResult((T)(object)new ImageSelection([Image], null));
            Assert.Equal("cfcChat.releaseImages", name);
            Released.AddRange(Assert.IsType<string[]>(Assert.Single(args!)));
            return ValueTask.FromResult(default(T)!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalPreviewsFollowConfirmedMessagesAcrossSessionsAndSkipAmbiguousHistory(bool overlapping)
    {
        var js = new PreviewBrowser();
        await using var previews = new LocalChatPreviews(js);
        using var page = new ChatWorkspace(new(http), access, access.Owner, previews);
        handler.Expect("GET", "sessions", Array.Empty<object>());
        await page.LoadAsync(null);
        await previews.SelectAsync(null, default);
        var removed = js.Image;
        await previews.RemoveAsync(null, removed);
        js.Image = new("blob:https://example.test/second", "second.jpg", 8);
        await previews.SelectAsync(null, default);
        handler.Expect("POST", "sessions", Session("one"));
        var post = handler.Hold("POST", "message", new { session_id = "one", content = "Repeated" });
        var send = page.SendAsync("Repeated");
        await post.Started.Task;
        var first = page.Active!;
        Assert.Equal(js.Image, Assert.Single(first.PendingImages));
        handler.Expect("POST", "sessions", Session("two"));
        await page.NewAsync();
        Assert.Empty(previews.Draft("two"));
        handler.Expect("PATCH", "sessions/one", Session("one", "Repeated"));
        post.Return(Reply());
        await send;
        Assert.Empty(previews.Draft("one"));
        Assert.Equal(js.Image, Assert.Single(previews.ForMessage("one", first.Messages[0])));
        Assert.Empty(previews.ForMessage("two", first.Messages[0]));
        Assert.Empty(previews.ForMessage("one", first.Messages[1]));

        using var reopened = new ChatWorkspace(new(http), access, access.Owner, previews);
        handler.Expect("GET", "sessions", new[] { Session("one") });
        handler.Expect("GET", "sessions/one", overlapping ? new[] {
            Row("one", "concurrent", "user", "Repeated"), Row("one", "saved-user", "user", "Repeated"),
            Row("one", "m2", "assistant", "Answer from API") } : new[] {
            Row("one", "saved-user", "user", "Repeated"), Row("one", "m2", "assistant", "Answer from API"),
            Row("one", "later", "user", "Repeated"), Row("one", "later-reply", "assistant", "Later answer") });
        ExpectRatings("one");
        await reopened.LoadAsync("one");
        var thread = reopened.Active!;
        if (overlapping) Assert.Empty(previews.ForMessage(thread.Id, thread.Messages[0]));
        else Assert.Equal(js.Image, Assert.Single(previews.ForMessage(thread.Id, thread.Messages[0])));
        Assert.Empty(previews.ForMessage(thread.Id, thread.Messages[1]));
        Assert.Empty(previews.ForMessage(thread.Id, thread.Messages[2]));
        await using var otherTab = new LocalChatPreviews(js);
        Assert.Empty(otherTab.ForMessage(thread.Id, thread.Messages[0]));
        handler.Expect("DELETE", "sessions/one", new { success = true });
        await reopened.DeleteAsync(thread);
        Assert.Equal(new[] { removed.Url, js.Image.Url }, js.Released);
        Assert.Empty(previews.ForMessage(thread.Id, thread.Messages[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSendKeepsUnsentDraftAndNeverGuessesTheOwnerOfARecoveredRow(bool stored)
    {
        var js = new PreviewBrowser();
        var previews = new LocalChatPreviews(js);
        using var page = new ChatWorkspace(new(http), access, access.Owner, previews);
        handler.Expect("GET", "sessions", new[] { Session("a") });
        var first = Row("a", "earlier", "user", "Earlier");
        handler.Expect("GET", "sessions/a", new[] { first });
        await page.LoadAsync("a");
        await previews.SelectAsync("a", default);
        handler.Fail("POST", "message", HttpStatusCode.ServiceUnavailable);
        handler.Expect("GET", "sessions/a", stored ? new[] { first, Row("a", "confirmed", "user", "Question") } : new[] { first });
        await page.SendAsync("Question");
        Assert.Equal(stored ? 0 : 1, previews.Draft("a").Count);
        Assert.Empty(previews.ForMessage("a", page.Active!.Messages.Last()));
        if (stored) Assert.Contains("Local previews were cleared", page.Active.Notice);
        await previews.DisposeAsync();
        Assert.Equal(js.Image.Url, Assert.Single(js.Released));
        Assert.Empty(previews.Draft("a"));
    }

    [Fact]
    public async Task DetailedListReflectsChatCreatesAndDeletesAndUsesTheCurrentOwnerToken()
    {
        using var history = NewPage();
        await OpenAsync();
        var first = handler.Hold("GET", "sessions?detail=true");
        first.Return(new[] { Session("a"), Session("private", owner: "other") });
        Assert.Equal("a", Assert.Single((await history.GetOwnedSessionsAsync(true))!).Id);
        Assert.Equal(access.Token, first.Bearer);

        handler.Expect("POST", "sessions", Session("new"));
        var created = await chat.NewAsync();
        access.Token = Guid.NewGuid().ToString("N");
        var refresh = handler.Hold("GET", "sessions?detail=true");
        refresh.Return(new[] { Session("new"), Session("a") });
        Assert.Equal(new[] { "new", "a" }, (await history.GetOwnedSessionsAsync(true))!.Select(row => row.Id));
        Assert.Equal(access.Token, refresh.Bearer);

        handler.Expect("DELETE", "sessions/new", new { success = true });
        handler.Expect("GET", "sessions/a", Array.Empty<object>());
        await chat.DeleteAsync(created!);
        handler.Expect("GET", "sessions?detail=true", new[] { Session("a") });
        Assert.Equal("a", Assert.Single((await history.GetOwnedSessionsAsync(true))!).Id);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("private")]
    public async Task UnavailableExplicitSelectionNeverLoadsAnotherChat(string id)
    {
        handler.Expect("GET", "sessions", new[] { Session("mine"), Session("private", owner: "other") });
        await chat.LoadAsync(id, requireSelection: true);
        Assert.Null(chat.Active);
        Assert.Contains("unavailable", chat.Notice);
        Assert.False(chat.CanSend);
        handler.Expect("GET", "sessions/mine", Array.Empty<object>());
        await chat.SelectAsync(Assert.Single(chat.Conversations));
        Assert.Null(chat.Notice);
        Assert.True(chat.CanSend);
    }

    [Fact]
    public async Task FailedDeepLinkLoadCannotLeaveThePreviousConversationOpen()
    {
        await OpenAsync(Row("a", "saved", "user", "Original question"));
        handler.Fail("GET", "sessions", HttpStatusCode.Forbidden);
        await chat.LoadAsync("different", requireSelection: true);
        Assert.Null(chat.Active);
        Assert.Contains("Access was denied", chat.Notice);
        Assert.False(chat.CanSend);
    }

    [Fact]
    public async Task LoadsRememberedSessionAndRestoresSavedMessagesOnRefresh()
    {
        for (var opening = 0; opening < 2; opening++)
        {
            using var page = NewPage();
            handler.Expect("GET", "sessions", new[] { Session("newest"), Session("chosen"), Session("foreign", owner: "other") });
            handler.Expect("GET", "sessions/chosen", new[] { Row("chosen", "m1", "user", "Question"), Row("chosen", "m2", "assistant", "Answer") });
            ExpectRatings("chosen", ("m2", -1), ("m1", 1), ("unknown", 1));
            await page.LoadAsync("chosen");
            Assert.Equal(2, page.Conversations.Count);
            var thread = page.Active!;
            Assert.Equal("chosen", thread.Id);
            Assert.Equal(new[] { "Question", "Answer" }, thread.Messages.Select(m => m.Content));
            var reply = thread.Messages[1];
            Assert.Equal(-1, reply.Rating);
            Assert.Null(thread.Messages[0].Rating);
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
        if (savedAnswer) ExpectRatings("a");
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
        var old = new[] { Row("a", "previous", "assistant", "Earlier answer") };
        ExpectOpening(old);
        ExpectRatings("a");
        await chat.LoadAsync("a");
        handler.Fail("POST", "message", HttpStatusCode.ServiceUnavailable);
        handler.Fail("GET", "sessions/a", HttpStatusCode.ServiceUnavailable);
        await chat.SendAsync("Try again");
        Assert.False(chat.CanSend);
        await chat.SendAsync("Try again");
        Assert.Equal(1, handler.MessagePosts);
        handler.Expect("GET", "sessions/a", old);
        ExpectRatings("a");
        await chat.SelectAsync(chat.Active!);
        Assert.True(chat.CanSend);
        handler.Expect("GET", "sessions/a", old);
        ExpectRatings("a");
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
        handler.Expect("POST", "feedback", new { success = true, score = 1 }, Vote("a", "m2", 1));
        await chat.ToggleFeedbackAsync(chat.Active, answer, 1);

        using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoBrowser>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ChatMessage.Sender)] = "CFC AI",
                [nameof(ChatMessage.Text)] = answer.Content,
                [nameof(ChatMessage.Rating)] = answer.Rating
            });
            var rendered = await renderer.RenderComponentAsync<ChatMessage>(parameters);
            return rendered.ToHtmlString();
        });
        Assert.Contains("chat-message bot", html);
        Assert.Contains("<p>Answer from API</p>", html);
        Assert.Contains("Copy message", html);
        Assert.Contains("aria-label=\"Mark as helpful\" aria-pressed=\"true\"", html);
        Assert.Contains("aria-label=\"Mark as not helpful\" aria-pressed=\"false\"", html);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task SavesChangesAndClearsOnlyAfterConfirmation(int thumb)
    {
        var history = new[] { Row("a", "reply", "assistant", "Answer") };
        ExpectOpening(history);
        ExpectRatings("a");
        await chat.LoadAsync("a");
        var thread = chat.Active!;
        var message = Assert.Single(thread.Messages);

        access.Token = Guid.NewGuid().ToString("N");
        var first = handler.Hold("POST", "feedback", Vote("a", "reply", thumb));
        var saving = chat.ToggleFeedbackAsync(thread, message, thumb);
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(access.Token, first.Bearer);
        Assert.Null(message.Rating);
        Assert.False(chat.CanRate(thread));
        Assert.True(chat.CanSend);
        await chat.ToggleFeedbackAsync(thread, message, thumb);
        first.Return(new { success = true, score = thumb });
        await saving;
        Assert.Equal(thumb, message.Rating);

        handler.Expect("POST", "feedback", new { success = true, score = -thumb }, Vote("a", "reply", -thumb));
        await chat.ToggleFeedbackAsync(thread, message, -thumb);
        Assert.Equal(-thumb, message.Rating);
        handler.Expect("POST", "feedback", new { success = true, score = (int?)null }, Vote("a", "reply", null));
        await chat.ToggleFeedbackAsync(thread, message, -thumb);
        Assert.Null(message.Rating);

        using var refreshed = NewPage();
        ExpectOpening(history);
        ExpectRatings("a");
        await refreshed.LoadAsync("a");
        Assert.Null(Assert.Single(refreshed.Active!.Messages).Rating);
        Assert.True(refreshed.CanRate(refreshed.Active));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "Access was denied")]
    public async Task UnavailableRatingsKeepHistoryReadableAndCanBeReloaded(HttpStatusCode status, string reason)
    {
        var history = new[] { Row("a", "reply", "assistant", "Saved answer") };
        ExpectOpening(history);
        handler.Fail("GET", "feedback?session_id=a", status);
        await chat.LoadAsync("a");
        var thread = chat.Active!;
        var message = Assert.Single(thread.Messages);
        Assert.Equal("Saved answer", message.Content);
        Assert.Contains(reason, thread.FeedbackError);
        Assert.True(chat.CanSend);
        Assert.False(chat.CanRate(thread));
        await chat.ToggleFeedbackAsync(thread, message, 1);

        ExpectRatings("a", ("reply", -1));
        await chat.ReloadFeedbackAsync(thread);
        Assert.Same(message, Assert.Single(thread.Messages));
        Assert.Equal(-1, message.Rating);
        Assert.Null(thread.FeedbackError);
        Assert.True(chat.CanRate(thread));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.OK)]
    public async Task UnconfirmedWriteKeepsPreviousRatingUntilReload(HttpStatusCode status)
    {
        var history = new[] { Row("a", "reply", "assistant", "Answer") };
        ExpectOpening(history);
        ExpectRatings("a", ("reply", 1));
        await chat.LoadAsync("a");
        var thread = chat.Active!;
        var message = Assert.Single(thread.Messages);
        if (status == HttpStatusCode.OK)
            handler.Expect("POST", "feedback", new { success = true, score = 1 }, Vote("a", "reply", -1));
        else
            handler.Fail("POST", "feedback", status);
        await chat.ToggleFeedbackAsync(thread, message, -1);
        Assert.Equal(1, message.Rating);
        Assert.Contains("could not be confirmed", thread.FeedbackError);
        if (status == HttpStatusCode.Forbidden) Assert.Contains("Access was denied", thread.FeedbackError);
        Assert.False(chat.CanRate(thread));

        // An empty map is authoritative too, e.g. after clearing the rating in another tab.
        ExpectRatings("a");
        await chat.ReloadFeedbackAsync(thread);
        Assert.Null(message.Rating);
        Assert.Null(thread.FeedbackError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingDuringSaveKeepsConversationRatingsSeparate(bool switchBackBeforeWriteCompletes)
    {
        var history = new[] { Row("a", "reply", "assistant", "First answer") };
        handler.Expect("GET", "sessions", new[] { Session("a"), Session("b") });
        handler.Expect("GET", "sessions/a", history);
        ExpectRatings("a");
        await chat.LoadAsync("a");
        var first = chat.Active!;
        var second = chat.Conversations[1];
        var write = handler.Hold("POST", "feedback", Vote("a", "reply", 1));
        var saving = chat.ToggleFeedbackAsync(first, first.Messages[0], 1);
        await write.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        handler.Expect("GET", "sessions/b", new[] { Row("b", "reply", "assistant", "Second answer") });
        ExpectRatings("b", ("reply", -1));
        await chat.SelectAsync(second);
        Assert.True(chat.CanRate(second));
        Assert.Equal(-1, Assert.Single(second.Messages).Rating);
        await chat.ToggleFeedbackAsync(second, first.Messages[0], 1);

        var reloading = Task.CompletedTask;
        if (switchBackBeforeWriteCompletes)
        {
            handler.Expect("GET", "sessions/a", history);
            reloading = chat.SelectAsync(first);
            Assert.False(reloading.IsCompleted);
            // Only allow the feedback GET after the pending write has been acknowledged.
            ExpectRatings("a", ("reply", 1));
        }
        write.Return(new { success = true, score = 1 });
        await Task.WhenAll(saving, reloading);
        Assert.Same(switchBackBeforeWriteCompletes ? first : second, chat.Active);
        Assert.Equal(1, Assert.Single(first.Messages).Rating);
        Assert.Equal(-1, Assert.Single(second.Messages).Rating);
    }

    [Fact]
    public async Task LateFeedbackLoadOnlyUpdatesTheOriginalConversation()
    {
        handler.Expect("GET", "sessions", new[] { Session("a"), Session("b") });
        handler.Expect("GET", "sessions/a", new[] { Row("a", "reply", "assistant", "First answer") });
        var read = handler.Hold("GET", "feedback?session_id=a");
        var opening = chat.LoadAsync("a");
        await read.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = chat.Active!;
        var second = chat.Conversations[1];
        handler.Expect("GET", "sessions/b", new[] { Row("b", "reply", "assistant", "Second answer") });
        ExpectRatings("b", ("reply", -1));
        await chat.SelectAsync(second);
        read.Return(new { feedback = new { reply = 1 } });
        await opening;
        Assert.Same(second, chat.Active);
        Assert.Equal(-1, Assert.Single(second.Messages).Rating);
        Assert.Equal(1, Assert.Single(first.Messages).Rating);
    }

    [Fact]
    public async Task PendingFeedbackLoadCannotApplyToAnotherAccountWithMatchingIds()
    {
        var history = new[] { Row("a", "reply", "assistant", "Answer"), Row("a", "user", "user", "Question") };
        ExpectOpening(history);
        var firstRead = handler.Hold("GET", "feedback?session_id=a");
        var loading = chat.LoadAsync("a");
        await firstRead.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(access.Token, firstRead.Bearer);

        var otherAccess = new TestAccess("other");
        using var other = new ChatWorkspace(new FastApiClient(http), otherAccess, otherAccess.Owner);
        handler.Expect("GET", "sessions", new[] { Session("a", owner: "other") });
        handler.Expect("GET", "sessions/a", history);
        var otherRead = handler.Hold("GET", "feedback?session_id=a");
        otherRead.Return(new { feedback = new { reply = -1 } });
        await other.LoadAsync("a");
        Assert.Equal(otherAccess.Token, otherRead.Bearer);
        firstRead.Return(new { feedback = new { reply = 1 } });
        await loading;
        var mine = chat.Active!;
        var theirs = other.Active!;
        Assert.Equal(1, mine.Messages[0].Rating);
        Assert.Equal(-1, theirs.Messages[0].Rating);

        // Foreign references and user messages must never produce a POST.
        await other.ToggleFeedbackAsync(mine, mine.Messages[0], -1);
        await other.ToggleFeedbackAsync(theirs, mine.Messages[0], -1);
        await other.ToggleFeedbackAsync(theirs, theirs.Messages[1], 1);
        var clear = handler.Hold("POST", "feedback", Vote("a", "reply", null));
        clear.Return(new { success = true, score = (int?)null });
        await other.ToggleFeedbackAsync(theirs, theirs.Messages[0], -1);
        Assert.Equal(otherAccess.Token, clear.Bearer);
        Assert.Null(theirs.Messages[0].Rating);
        Assert.Equal(1, mine.Messages[0].Rating);
    }

    private Task OpenAsync(params object[] history)
    {
        ExpectOpening(history);
        return chat.LoadAsync("a");
    }

    private void ExpectOpening(params object[] history)
    {
        handler.Expect("GET", "sessions", new[] { Session("a") });
        handler.Expect("GET", "sessions/a", history);
    }

    private void ExpectRatings(string session, params (string Id, int Score)[] ratings) =>
        handler.Expect("GET", $"feedback?session_id={session}", new { feedback = ratings.ToDictionary(r => r.Id, r => r.Score) });

    private static object Vote(string session, string message, int? rating) =>
        new { message_id = message, session_id = session, rating };

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
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
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

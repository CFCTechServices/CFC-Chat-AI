using System.Net;
using System.Net.Http.Json;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public void HistoryUsesInclusiveRollingUtcBoundaries(int days)
    {
        var now = new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);
        var start = now.AddDays(-days);
        ChatSession At(string id, DateTimeOffset instant) => new(id, "user-a", instant.ToString("O"));
        ChatSession[] rows =
        [
            At("future", now.AddTicks(1)), At("now", now),
            At("inside", start.AddTicks(1)), At("boundary", start.ToOffset(TimeSpan.FromHours(-5))),
            At("older", start.AddTicks(-1)), new("unknown", "user-a", "invalid")
        ];
        Assert.Equal(new[] { "now", "inside", "boundary" }, ConversationHistory.Filter(rows, "", days, now).Select(r => r.Id));
        Assert.Equal(rows.Length, ConversationHistory.Filter(rows, "", 0, now).Count);
    }

    [Fact]
    public async Task HistorySearchesTitlesAndPreviewsCombinesFiltersAndHandlesMissingMetadata()
    {
        browser.Session = Token();
        await session.RestoreAsync();
        using var http = HistoryHttp(req =>
        {
            Assert.Equal("/api/chat/sessions?detail=true", req.RequestUri!.PathAndQuery);
            Assert.Equal(browser.Session, req.Headers.Authorization!.Parameter);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new object[]
            {
                new { id = "recent", user_id = "user-a", title = "Quarterly review", last_message = "Hello",
                    created_at = clock.Now.AddHours(-2).ToString("O"), message_count = 1 },
                new { id = "older", user_id = "user-a", title = "Saved discussion", last_message = "Quarterly totals",
                    created_at = clock.Now.AddDays(-8).ToString("O"), message_count = 3 },
                new { id = "empty", user_id = "user-a", message_count = 0 },
                new { id = "partial", user_id = "user-a", created_at = "unknown" },
                new { id = "foreign", user_id = "other", title = "Private conversation" }
            }) };
        });
        using var services = HistoryServices(http, new TestNavigation("history"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(History)));
            await page.QuiescenceTask;
            var html = page.ToHtmlString();
            Assert.Contains("href=\"/chat/older\"", html);
            Assert.Contains("Untitled Chat", html);
            Assert.Contains("Creation date unavailable", html);
            Assert.Contains("No preview available", html);
            Assert.Contains("Message count unavailable", html);
            Assert.Contains("0 messages", html);
            Assert.Contains("1 message<", html);
            Assert.Contains("3 messages", html);
            Assert.Contains("8d ago", html);
            Assert.DoesNotContain("Private conversation", html);
            await renderer.InputAsync("  quaRterLy  ");
            Assert.Contains("Quarterly review", page.ToHtmlString());
            Assert.Contains("Saved discussion", page.ToHtmlString());
            Assert.DoesNotContain("Untitled Chat", page.ToHtmlString());
            await renderer.SelectAsync("7");
            Assert.DoesNotContain("Saved discussion", page.ToHtmlString());
            Assert.Contains("Quarterly review", page.ToHtmlString());
            await renderer.InputAsync("absent");
            Assert.Contains("No conversations match", page.ToHtmlString());
        });
    }

    [Fact]
    public async Task HistoryDistinguishesLoadingAndFailureFromAnEmptyListAndAllowsRetry()
    {
        browser.Session = Token();
        await session.RestoreAsync();
        var first = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using var http = new HttpClient(new HistoryHandler(_ => ++requests == 1 ? first.Task :
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) })))
            { BaseAddress = new Uri("https://fixture.invalid/") };
        using var services = HistoryServices(http, new TestNavigation("history"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(History), ParameterView.Empty);
            Assert.Contains("Loading conversations", page.ToHtmlString());
            first.SetResult(new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { detail = "unavailable" }) });
            await page.QuiescenceTask;
            Assert.Contains("could not be loaded", page.ToHtmlString());
            Assert.DoesNotContain("no saved conversations", page.ToHtmlString());
            await renderer.ClickButtonAsync();
            Assert.Contains("no saved conversations", page.ToHtmlString());
            Assert.DoesNotContain("could not be loaded", page.ToHtmlString());
            Assert.Equal(2, requests);
        });
    }

    [Fact]
    public async Task DeepLinkAndRefreshOpenAnOlderConversationAheadOfTabMemory()
    {
        browser.Session = Token();
        var storage = new ChatStorage(browser);
        var historyReads = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/chat/sessions")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Enumerable.Range(0, 60)
                    .Select(i => new { id = $"saved-{i}", user_id = "user-a", title = $"Chat {i}", created_at = "2026-03-01" })) };
            string id = req.RequestUri.Segments.Last();
            Assert.Contains(id, new[] { "saved-59", "saved-42" });
            historyReads++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[]
            {
                new { id = "m1", session_id = id, role = "user", content = $"Question from {id}", created_at = "2026-03-01" }
            }) };
        });
        for (var visit = 0; visit < 3; visit++)
        {
            var linked = visit < 2;
            var navigation = new TestNavigation(linked ? "chat/saved-59" : "chat");
            await using var circuit = new SupabaseSessionService(backend, browser, clock);
            using var services = HistoryServices(http, navigation, storage, circuit);
            await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var values = new Dictionary<string, object?>();
                if (linked) values[nameof(Chat.SessionId)] = "saved-59";
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(SessionRouteView.RouteData)] = new RouteData(typeof(Chat), values)
                });
                var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), parameters);
                await page.QuiescenceTask;
                var expected = linked ? "saved-59" : "saved-42";
                Assert.Contains($"Question from {expected}", page.ToHtmlString());
                Assert.Equal(expected, storage.Remembered);
                if (visit == 1)
                {
                    await renderer.ClickChatButtonAsync("Chat 42");
                    Assert.Contains("Question from saved-42", page.ToHtmlString());
                    Assert.EndsWith("/chat/saved-42", navigation.Uri);
                    Assert.Equal("saved-42", storage.Remembered);
                }
            });
        }
        Assert.Equal(4, historyReads);
    }

    private ServiceProvider HistoryServices(HttpClient http, NavigationManager navigation, IJSRuntime js, SupabaseSessionService? circuit = null) =>
        new ServiceCollection().AddLogging().AddSingleton(circuit ?? session).AddSingleton<TimeProvider>(clock)
            .AddSingleton(new FastApiClient(http)).AddKeyedSingleton(FastApiClient.UploadClientKey, new FastApiClient(http))
            .AddSingleton(navigation).AddSingleton(js).BuildServiceProvider();

    private static HttpClient HistoryHttp(Func<HttpRequestMessage, HttpResponseMessage> reply) =>
        new(new HistoryHandler(req => Task.FromResult(reply(req)))) { BaseAddress = new Uri("https://fixture.invalid/") };

    private sealed class HistoryHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => reply(req).WaitAsync(ct);
    }

    private sealed class ChatStorage(IJSRuntime browserJs) : IJSRuntime
    {
        public string? Remembered { get; private set; } = "saved-0";
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args)
        {
            if (name.StartsWith("sessionStorage.", StringComparison.Ordinal))
            {
                Assert.Equal("cfc-chat-session:user-a", args![0]);
                if (name == "sessionStorage.getItem") return ValueTask.FromResult((T)(object)Remembered!);
                Remembered = args.Length > 1 ? (string?)args[1] : null;
                return ValueTask.FromResult(default(T)!);
            }
            if (name == "cfcChat.scrollToBottom" || name.StartsWith("Blazor._internal.domWrapper.focus", StringComparison.Ordinal))
                return ValueTask.FromResult(default(T)!);
            return browserJs.InvokeAsync<T>(name, ct, args);
        }
    }
}

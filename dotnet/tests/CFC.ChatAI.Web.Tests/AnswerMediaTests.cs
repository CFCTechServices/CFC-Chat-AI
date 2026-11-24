using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    [Theory]
    [InlineData("docs/manual/a b.png", "http://localhost:8000/dev/api/chat/content/images/docs/manual/a%20b.png")]
    [InlineData("/api/chat/content/images/images/a%20b.png", "http://localhost:8000/dev/api/chat/content/images/images/a%20b.png")]
    [InlineData("https://images.example.test/a.png?v=2", "https://images.example.test/a.png?v=2")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/svg+xml,test", null)]
    [InlineData("file:///example.png", null)]
    [InlineData("//images.example.test/a.png", null)]
    [InlineData("https://name@images.example.test/a.png", null)]
    [InlineData("docs/%2e%2e/outside.png", null)]
    [InlineData("images/a%2fb.png", null)]
    [InlineData("docs/a\\b.png", null)]
    [InlineData("images/", null)]
    public void AnswerImageUrlsUseTheConfiguredBackendAndSupportedSchemes(string path, string? expected)
    {
        var metadata = new ChatMessageMetadata(RelevantImages: [new ChatImage(path)]);
        var images = AnswerMedia.Images("text", metadata, new Uri("http://localhost:8000/dev/"));
        Assert.Equal(expected, images.SingleOrDefault()?.Url.AbsoluteUri);
    }

    [Fact]
    public void PlacementUsesPythonCharacterIndicesAndAppendsOutOfRangeHintsInReturnOrder()
    {
        const string text = "A😀B";
        var metadata = new ChatMessageMetadata(RelevantImages:
        [
            new("images/past.png", 40), new("images/after-emoji.png", 2), new("images/start.png", 0),
            new("images/end.png", 3), new("images/no-hint.png"), new("images/negative.png", -1)
        ]);
        var images = AnswerMedia.Images(text, metadata, new Uri("http://localhost:8000/"));
        Assert.Equal(new[] { 0, 3, 4, 4, 4, 4 }, images.Select(i => i.Offset));
        Assert.Equal(new[] { "start.png", "after-emoji.png", "past.png", "end.png", "no-hint.png", "negative.png" },
            images.Select(i => i.Url.Segments.Last()));
    }

    [Theory]
    [InlineData("A **bold** word", 4, 10)]
    [InlineData("A **bold** word", 11, 11)]
    [InlineData("- list item\nNext", 4, 11)]
    public void ImageHintsKeepMarkdownEmphasisAndListItemsIntact(string text, int hint, int offset)
    {
        var metadata = new ChatMessageMetadata(RelevantImages: [new("images/example.png", hint)]);
        Assert.Equal(offset, Assert.Single(AnswerMedia.Images(text, metadata, new Uri("http://localhost:8000/"))).Offset);
    }

    [Fact]
    public void MissingImageListCanUseLegacyCitationPathsButExplicitEmptyListStaysEmpty()
    {
        var citations = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(
            """[{"source":"Guide","image_paths":["images/old.png",null,"images/old.png"]}]""")!;
        var metadata = new ChatMessageMetadata(citations);
        var backend = new Uri("https://fixture.invalid/");
        Assert.Equal(4, Assert.Single(AnswerMedia.Images("text", metadata, backend)).Offset);
        Assert.Empty(AnswerMedia.Images("text", metadata with { RelevantImages = [] }, backend));
        Assert.Equal("Guide", Assert.Single(AnswerMedia.Sources(citations)));
    }

    [Fact]
    public async Task NewAndReopenedAnswersRenderTheirOwnImagesAndSourcesInTheSamePositions()
    {
        browser.Session = Token();
        const string answer = "First 🐈.\nSecond **step**.";
        object[] sources = [7, new { source = "Guide <test>", section_title = "Setup", chunk_id = "c1", image_paths = new[] { "images/unselected.png" } }];
        object[] images =
        [
            new { path = "images/end.png", position = "bad" },
            null!, 3, new { path = false }, new { path = "javascript:bad" },
            new { path = "docs/manual/step one.png", position = 9, alt_text = "Screen <one>" }
        ];
        var media = new { citations = sources, relevant_images = images };
        var posts = 0;
        using var http = HistoryHttp(req =>
        {
            object Row(string id, string role, string content, object? metadata) =>
                new { session_id = "a", id, role, content, created_at = "2026-01-01", metadata };
            switch (req.RequestUri!.PathAndQuery)
            {
                case "/api/chat/sessions":
                    return Ok(new[] { new { id = "a", user_id = "user-a", title = "Session", created_at = "2026-01-01" } });
                case "/api/chat/sessions/a":
                    var rows = new List<object>
                    {
                        Row("u1", "user", "Earlier question", new { relevant_images = new[] { new { path = "images/user.png" } } }),
                        Row("a1", "assistant", "Earlier answer", new { relevant_images = new[] { new { path = "images/earlier.png" } } }),
                        Row("a2", "assistant", "Ordinary **text**", "malformed legacy metadata"),
                        Row("a3", "assistant", "Still readable", new { citations = sources, relevant_images = "bad list" })
                    };
                    if (posts > 0) rows.Add(Row("reply", "assistant", answer, media));
                    return Ok(rows);
                case "/api/chat/feedback?session_id=a":
                    return Ok(new { feedback = new { a1 = 1 } });
                case "/api/chat/message":
                    Assert.Equal(HttpMethod.Post, req.Method);
                    posts++;
                    return Ok(new { id = "reply", role = "assistant", content = answer, created_at = "2026-01-01",
                        citations = sources, relevant_images = images });
                default:
                    throw new InvalidOperationException("Unexpected API request.");
            }
        });
        http.BaseAddress = new Uri("http://localhost:8000/");
        for (var visit = 0; visit < 2; visit++)
        {
            await using var circuit = new SupabaseSessionService(backend, browser, clock);
            using var services = HistoryServices(http, new TestNavigation("chat"), new ChatStorage(browser), circuit);
            await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Chat)));
                await page.QuiescenceTask;
                if (visit == 0)
                {
                    await renderer.InputAsync("Show the step");
                    await renderer.PressEnterAsync();
                }
                var html = page.ToHtmlString();
                Assert.DoesNotContain("images/user.png", html);
                Assert.DoesNotContain("unselected.png", html);
                Assert.DoesNotContain("javascript:bad", html);
                var bubbles = Regex.Split(html, "<div class=\"chat-message (?:user|bot)\">");
                var earlier = Assert.Single(bubbles, b => b.Contains("Earlier answer"));
                Assert.Contains("/images/earlier.png", earlier);
                Assert.Contains("aria-pressed=\"true\"", earlier);
                Assert.DoesNotContain("step%20one", earlier);
                var ordinary = Assert.Single(bubbles, b => b.Contains("Ordinary <strong>text</strong>"));
                Assert.DoesNotContain("<img", ordinary);
                Assert.DoesNotContain("<summary>", ordinary);
                var recoveredText = Assert.Single(bubbles, b => b.Contains("Still readable"));
                Assert.DoesNotContain("<img", recoveredText);
                Assert.Contains("Guide &lt;test&gt;", recoveredText);
                var reply = Assert.Single(bubbles, b => b.Contains("Second <strong>step</strong>"));
                Assert.DoesNotContain("earlier.png", reply);
                Assert.Contains("Screen &lt;one&gt;", reply);
                Assert.Contains("Guide &lt;test&gt;", reply);
                Assert.Contains("Setup", reply);
                var first = reply.IndexOf("<p>First", StringComparison.Ordinal);
                var inline = reply.IndexOf("http://localhost:8000/api/chat/content/images/docs/manual/step%20one.png", StringComparison.Ordinal);
                var second = reply.IndexOf("<p>Second <strong>step</strong>.</p>", StringComparison.Ordinal);
                Assert.True(first >= 0 && inline > first);
                Assert.True(inline < second && second < reply.IndexOf("images/end.png", StringComparison.Ordinal));
            });
        }
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task UnavailableImageReplacesItsThumbnailAndClosesItsPreview()
    {
        using var services = NavigationServices(session, new TestNavigation());
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(ChatAnswerMedia), ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ChatAnswerMedia.Image)] = new AnswerImage(new Uri("https://fixture.invalid/example.png"), "Example screen", 0)
            }));
            await page.QuiescenceTask;
            await renderer.ClickButtonAsync();
            Assert.Contains("role=\"dialog\"", page.ToHtmlString());
            await renderer.ImageErrorAsync();
            var html = page.ToHtmlString();
            Assert.Contains("Image unavailable (Example screen)", html);
            Assert.DoesNotContain("<img", html);
            Assert.DoesNotContain("role=\"dialog\"", html);
        });
    }

    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}

using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using CFC.ChatAI.Web.Components.Layout;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("dev")]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task RestoredChatRendersOneLogoAndOnlyImplementedNavigation(string role)
    {
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = role });
        await session.RestoreAsync();

        using var http = new HttpClient(); // Static rendering must not fetch chat history.
        using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton(new FastApiClient(http)).AddSingleton<IJSRuntime>(browser)
            .AddSingleton<NavigationManager, TestNavigation>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<SessionRouteView>(RouteParameters(typeof(Chat)));
            var html = page.ToHtmlString();
            Assert.Single(Regex.Matches(html, "<header\\b"));
            Assert.Single(Regex.Matches(html, "src=\"images/logo-cfc\\.png\""));
            Assert.Contains("New Chat", html);
            Assert.Contains("Sign Out", html);
            var nav = Regex.Match(html, "<nav aria-label=\"Primary navigation\"[^>]*>(.*?)</nav>", RegexOptions.Singleline);
            Assert.True(nav.Success);
            var isAdmin = role is "admin" or "superuser";
            Assert.Equal(isAdmin ? 4 : 3, Regex.Matches(nav.Value, "<a\\b").Count);
            Assert.DoesNotContain("Docs", nav.Value, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/chat\"", nav.Value);
            Assert.Contains("href=\"/history\"", nav.Value);
            Assert.Contains("href=\"/settings\"", nav.Value);
            Assert.Equal(isAdmin, nav.Value.Contains("href=\"/admin\""));
            Assert.Contains("aria-current=\"page\"", nav.Value);
        });
    }

    [Theory]
    [InlineData(typeof(Chat), null, "login")]
    [InlineData(typeof(History), null, "login")]
    [InlineData(typeof(Settings), null, "login")]
    [InlineData(typeof(Admin), null, "login")]
    [InlineData(typeof(Admin), "user", "chat")]
    [InlineData(typeof(Admin), "dev", "chat")]
    [InlineData(typeof(Admin), "Admin", "chat")]
    public async Task DirectNavigationDeniesAccessBeforeRendering(Type pageType, string? role, string redirect)
    {
        browser.Session = role is null ? null : Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = role! });
        var navigation = new TestNavigation(AppDestination.ForPage(pageType)!.Path.TrimStart('/'));
        using var services = NavigationServices(session, navigation);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(pageType));
            await page.QuiescenceTask;
            var html = page.ToHtmlString();
            Assert.DoesNotContain("Admin Console", html);
            Assert.DoesNotContain("New Chat", html);
            Assert.DoesNotContain("Search conversations", html);
            Assert.DoesNotContain("Save profile", html);
            Assert.DoesNotContain("Primary navigation", html);
            Assert.EndsWith("/" + redirect, navigation.Uri);
        });
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task DirectAdminRouteWaitsForVerifiedRoleAndSharedSignOutSurvivesRefresh(string role)
    {
        browser.Session = Token();
        var verification = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Profile = _ => verification.Task;
        var navigation = new TestNavigation("admin");
        using var http = HistoryHttp(_ => AdminList());
        using var services = HistoryServices(http, navigation, browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            Assert.Contains("Checking access", page.ToHtmlString());
            Assert.DoesNotContain("Admin Console", page.ToHtmlString());
            // Quiescence does not wait for the browser restoration in OnAfterRenderAsync.
            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRestored() { if (session.CanAccessChat) restored.TrySetResult(); }
            session.Changed += OnRestored;
            verification.SetResult(ActiveProfile() with { Role = role });
            try { await restored.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { session.Changed -= OnRestored; }
            Assert.Contains("Admin Console", page.ToHtmlString());
            Assert.Contains("Sign Out", page.ToHtmlString());
            Assert.Contains("href=\"/admin\"", page.ToHtmlString());
            await renderer.ClickButtonAsync<AppNavigation>("Sign Out");
            Assert.Contains("signOut", browser.Calls);
            Assert.Null(browser.Session);
            Assert.DoesNotContain("Admin Console", page.ToHtmlString());
            Assert.DoesNotContain("Primary navigation", page.ToHtmlString());
            Assert.EndsWith("/login", navigation.Uri);
        });

        await using var refreshed = new SupabaseSessionService(backend, browser, clock);
        var reopened = new TestNavigation("chat");
        using var freshServices = NavigationServices(refreshed, reopened);
        await using var freshRenderer = new LoginRenderer(freshServices, freshServices.GetRequiredService<ILoggerFactory>());
        await freshRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = freshRenderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Chat)));
            await page.QuiescenceTask;
            Assert.DoesNotContain("New Chat", page.ToHtmlString());
            Assert.EndsWith("/login", reopened.Uri);
        });
    }

    [Fact]
    public async Task ExpiredSessionWithCachedAdminProfileCannotPrerenderProtectedPage()
    {
        browser.Session = Token(minutes: 1);
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = "admin" });
        await session.RestoreAsync();
        clock.Now = clock.Now.AddMinutes(2);
        using var services = NavigationServices(session, new TestNavigation("admin"));
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<SessionRouteView>(RouteParameters(typeof(Admin)));
            Assert.DoesNotContain("Admin Console", page.ToHtmlString());
            Assert.DoesNotContain("Primary navigation", page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(typeof(History), "/history")]
    [InlineData(typeof(Settings), "/settings")]
    public void PlannedMemberRoutesRequireAnActiveProfile(Type pageType, string path)
    {
        var destination = Assert.IsType<AppDestination>(AppDestination.ForPage(pageType));
        Assert.Equal(path, destination.Path);
        Assert.False(destination.Allows(null));
        Assert.False(destination.Allows(ActiveProfile() with { Role = "superuser", Status = "inactive" }));
        Assert.True(destination.Allows(ActiveProfile()));
    }

    [Fact]
    public async Task ReverifiedRoleDowngradeRemovesOpenAdminPage()
    {
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = "admin" });
        var navigation = new TestNavigation("admin");
        using var http = HistoryHttp(_ => AdminList());
        using var services = HistoryServices(http, navigation, browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            await page.QuiescenceTask;
            Assert.Contains("Admin Console", page.ToHtmlString());
            backend.Profile = _ => Task.FromResult(ActiveProfile());
            await session.RestoreAsync();
            Assert.DoesNotContain("Admin Console", page.ToHtmlString());
            Assert.EndsWith("/chat", navigation.Uri);
        });
    }

    private ServiceProvider NavigationServices(SupabaseSessionService circuit, NavigationManager navigation) =>
        new ServiceCollection().AddLogging().AddSingleton(circuit).AddSingleton<IJSRuntime>(browser)
            .AddSingleton(navigation).BuildServiceProvider();

    private static ParameterView RouteParameters(Type pageType) => ParameterView.FromDictionary(
        new Dictionary<string, object?> { [nameof(SessionRouteView.RouteData)] = new RouteData(pageType, new Dictionary<string, object?>()) });

}

using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
            Assert.Equal(2, Regex.Matches(nav.Value, "<a\\b").Count);
            Assert.Contains("href=\"/chat\"", nav.Value);
            Assert.Contains("href=\"/history\"", nav.Value);
            Assert.Contains("aria-current=\"page\"", nav.Value);
        });
    }

    [Theory]
    [InlineData(typeof(Chat), null, "login")]
    [InlineData(typeof(History), null, "login")]
    [InlineData(typeof(AdminRouteProbe), null, "login")]
    [InlineData(typeof(AdminRouteProbe), "user", "chat")]
    [InlineData(typeof(AdminRouteProbe), "Admin", "chat")]
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
            Assert.DoesNotContain("Admin route probe", html);
            Assert.DoesNotContain("New Chat", html);
            Assert.DoesNotContain("Search conversations", html);
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
        using var services = NavigationServices(session, navigation);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(AdminRouteProbe)));
            Assert.Contains("Checking access", page.ToHtmlString());
            Assert.DoesNotContain("Admin route probe", page.ToHtmlString());
            // Quiescence does not wait for the browser restoration in OnAfterRenderAsync.
            var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRestored() { if (session.CanAccessChat) restored.TrySetResult(); }
            session.Changed += OnRestored;
            verification.SetResult(ActiveProfile() with { Role = role });
            try { await restored.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { session.Changed -= OnRestored; }
            Assert.Contains("Admin route probe", page.ToHtmlString());
            Assert.Contains("Sign Out", page.ToHtmlString());
            Assert.DoesNotContain("href=\"/admin\"", page.ToHtmlString());
            await renderer.ClickButtonAsync();
            Assert.Contains("signOut", browser.Calls);
            Assert.Null(browser.Session);
            Assert.DoesNotContain("Admin route probe", page.ToHtmlString());
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
            var page = await renderer.RenderComponentAsync<SessionRouteView>(RouteParameters(typeof(AdminRouteProbe)));
            Assert.DoesNotContain("Admin route probe", page.ToHtmlString());
            Assert.DoesNotContain("Primary navigation", page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(typeof(History), "/history")]
    [InlineData(typeof(SettingsRouteProbe), "/settings")]
    [InlineData(typeof(DocsRouteProbe), "/docs")]
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
        using var services = NavigationServices(session, navigation);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(AdminRouteProbe)));
            await page.QuiescenceTask;
            Assert.Contains("Admin route probe", page.ToHtmlString());
            backend.Profile = _ => Task.FromResult(ActiveProfile());
            await session.RestoreAsync();
            Assert.DoesNotContain("Admin route probe", page.ToHtmlString());
            Assert.EndsWith("/chat", navigation.Uri);
        });
    }

    private ServiceProvider NavigationServices(SupabaseSessionService circuit, NavigationManager navigation) =>
        new ServiceCollection().AddLogging().AddSingleton(circuit).AddSingleton<IJSRuntime>(browser)
            .AddSingleton(navigation).BuildServiceProvider();

    private static ParameterView RouteParameters(Type pageType) => ParameterView.FromDictionary(
        new Dictionary<string, object?> { [nameof(SessionRouteView.RouteData)] = new RouteData(pageType, new Dictionary<string, object?>()) });

    // The test assembly is outside the app router. Exercise the real guard without publishing an admin page.
    [Route("/admin")]
    public sealed class AdminRouteProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "Admin route probe");
    }

    [Route("/settings")] private sealed class SettingsRouteProbe : ComponentBase { }
    [Route("/docs")] private sealed class DocsRouteProbe : ComponentBase { }
}

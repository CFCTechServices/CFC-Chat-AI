using System.Reflection;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    [Fact]
    public void SupportedFrontendRoutesHaveBlazorPagesAndDocsIsNotRoutable()
    {
        var routes = typeof(Chat).Assembly.ExportedTypes.SelectMany(type =>
            type.GetCustomAttributes<RouteAttribute>().Select(attribute => (attribute.Template, Page: type)))
            .ToDictionary(route => route.Template, route => route.Page);
        Assert.DoesNotContain("/docs", routes.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, page) in new (string, Type)[] { ("/", typeof(Home)), ("/login", typeof(Login)),
            ("/ui", typeof(Home)), ("/transition", typeof(Transition)), ("/chat", typeof(Chat)), ("/chat/{SessionId}", typeof(Chat)), ("/admin", typeof(Admin)),
            ("/settings", typeof(Settings)), ("/history", typeof(History)), ("/reset-password", typeof(ResetPassword)) })
            Assert.Equal(page, routes[path]);
    }

    [Theory]
    [InlineData(typeof(Login), "user", "docs", "/chat")]
    [InlineData(typeof(Login), "admin", "docs", "/admin")]
    [InlineData(typeof(Login), "admin", "chat", "/chat")]
    [InlineData(typeof(Login), "superuser", "history", "/history")]
    [InlineData(typeof(Transition), "user", "admin", "/chat")]
    [InlineData(typeof(Transition), "dev", "//outside.invalid", "/chat")]
    [InlineData(typeof(Transition), "admin", "auto", "/admin")]
    public async Task EntryRouteHonorsOnlyVerifiedStartingPages(Type pageType, string role, string preference, string destination)
    {
        browser.StartingPage = preference;
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = role });
        var nav = new TestNavigation(pageType == typeof(Login) ? "login" : "transition");
        using var services = NavigationServices(session, nav);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(pageType));
            await page.QuiescenceTask;
            Assert.Equal(nav.BaseUri.TrimEnd('/') + destination, nav.Uri);
        });
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("user", false)]
    public async Task SettingsOffersBrowserPreferencesWithRoleScopedChoices(string role, bool admin)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Preferences require no backend request"));
        using var services = HistoryServices(http, new TestNavigation("settings"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Settings)));
            await page.QuiescenceTask;
            var html = page.ToHtmlString();
            Assert.Contains("data-preference=\"text-size\"", html);
            Assert.Contains("data-preference=\"theme\"", html);
            Assert.DoesNotContain("<option value=\"docs\"", html);
            Assert.Equal(admin, html.Contains("<option value=\"admin\""));
            Assert.Contains("Update password", html);
            await renderer.ClickButtonAsync<Settings>("Show new password");
            Assert.Contains("id=\"settings-password\" type=\"text\"", page.ToHtmlString());
            Assert.Contains("id=\"settings-confirmation\" type=\"password\"", page.ToHtmlString());
            await renderer.ClickButtonAsync<Settings>("Hide new password");
            Assert.Contains("id=\"settings-password\" type=\"password\"", page.ToHtmlString());
        });
    }
}

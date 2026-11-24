using System.Net;
using System.Reflection;
using System.Text;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Services;
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
            ("/transition", typeof(Transition)), ("/chat", typeof(Chat)), ("/admin", typeof(Admin)),
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
    [InlineData("success", true)]
    [InlineData("unavailable", false)]
    [InlineData("offline", false)]
    [InlineData("wrong-content", false)]
    [InlineData("same-origin-only", false)]
    [InlineData("frame-denied", false)]
    [InlineData("allowed-origin", true)]
    public async Task BackendSwaggerProbeUsesConfiguredAddressAndNoBearer(string scenario, bool canEmbed)
    {
        var requests = 0;
        using var http = HistoryHttp(req =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://fixture.invalid/backend/docs", req.RequestUri!.AbsoluteUri);
            Assert.Null(req.Headers.Authorization);
            if (scenario == "offline") throw new HttpRequestException();
            var response = new HttpResponseMessage(scenario == "unavailable" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent("fixture", Encoding.UTF8, scenario == "wrong-content" ? "application/json" : "text/html")
            };
            if (scenario == "same-origin-only") response.Headers.Add("X-Frame-Options", "SAMEORIGIN");
            if (scenario == "frame-denied") response.Headers.Add("Content-Security-Policy", "frame-ancestors 'none'");
            if (scenario == "allowed-origin") response.Headers.Add("Content-Security-Policy", "frame-ancestors https://example.test");
            return response;
        });
        http.BaseAddress = new Uri("https://fixture.invalid/backend/");
        var api = new FastApiClient(http);
        Assert.Equal(canEmbed, await api.CanEmbedDocsAsync(new Uri("https://example.test/"), default));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task DocsCannotEmbedItselfOrMixedContent()
    {
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unexpected request"));
        var api = new FastApiClient(http);
        Assert.False(await api.CanEmbedDocsAsync(api.DocsAddress!, default));
        http.BaseAddress = new Uri("http://insecure.invalid/");
        Assert.False(await api.CanEmbedDocsAsync(new Uri("https://app.invalid/docs"), default));
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

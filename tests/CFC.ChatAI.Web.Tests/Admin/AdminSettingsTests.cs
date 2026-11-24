using System.Net;
using System.Net.Http.Json;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    private static readonly AdminSystemSettings InitialFlags = new(false, true, false);

    private static HttpResponseMessage SettingsJson(object data) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task AdminTabsLoadSavedFlagsChangeOneAndPreserveUsersAndContent(string role)
    {
        await RestoreRole(role);
        var reads = 0;
        var writes = 0;
        var flags = InitialFlags;
        using var http = HistoryHttp(req =>
        {
            switch (req.RequestUri!.AbsolutePath)
            {
                case "/api/admin/users": return AdminList(Target);
                case "/api/admin/documents": return DocumentList(Guide);
                case "/api/health":
                    Assert.Null(req.Headers.Authorization);
                    return SettingsJson(new ApiHealthResponse(true, "Running", "test-version"));
                case "/api/admin/settings":
                    Assert.Equal(browser.Session, req.Headers.Authorization?.Parameter);
                    if (req.Method == HttpMethod.Patch)
                    {
                        writes++;
                        flags = flags with { EmailNotifications = false };
                    }
                    else { Assert.Equal(HttpMethod.Get, req.Method); reads++; }
                    return SettingsJson(flags);
                default: throw new InvalidOperationException("Unexpected route.");
            }
        });
        using var services = HistoryServices(http, new TestNavigation("admin"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            await page.QuiescenceTask;
            await renderer.ClickButtonAsync<Admin>("Settings");
            var html = page.ToHtmlString();
            Assert.Contains("Auto-approve uploads", html);
            Assert.Contains("Maintenance mode", html);
            Assert.Contains("aria-label=\"Email notifications\" aria-checked=\"true\"", html);
            Assert.Contains("require backend implementation", html);
            Assert.Contains("Healthy", html);
            Assert.Contains("test-version", html);
            await renderer.ClickButtonAsync<AdminSettings>("On");
            Assert.Contains("Setting saved.", page.ToHtmlString());
            Assert.Contains("aria-label=\"Email notifications\" aria-checked=\"false\"", page.ToHtmlString());
            Assert.Equal(1, writes);
            Assert.Equal(2, reads);
            await renderer.ClickButtonAsync<Admin>("Content");
            Assert.Contains(Guide.Name, page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Users");
            Assert.Contains(Target.Email, page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Settings");
            Assert.Equal(3, reads);
            Assert.Contains("aria-label=\"Email notifications\" aria-checked=\"false\"", page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData("user", "user-a")]
    [InlineData("dev", "user-a")]
    [InlineData("superuser", "another-account")]
    public async Task SettingsPanelRequiresVerifiedAdminOwner(string role, string owner)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unauthorized transport."));
        using var work = new AdminSettingsWorkspace(new(http), session, owner, clock);
        await work.LoadSettingsAsync();
        await work.UpdateAsync(new(MaintenanceMode: true));
        await work.CheckHealthAsync();
        Assert.Null(work.Settings);
        Assert.Null(work.Health);
        Assert.NotNull(work.SettingsError);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("update unavailable")]
    [InlineData("readback unavailable")]
    [InlineData("concurrent change")]
    [InlineData("invalid acknowledgement")]
    public async Task SettingsFailuresNeverReportSavedOrReplayWrites(string scenario)
    {
        await RestoreRole("admin");
        var updated = InitialFlags with { MaintenanceMode = true };
        var writes = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.Method == HttpMethod.Patch)
            {
                writes++;
                return scenario switch
                {
                    "forbidden" => new(HttpStatusCode.Forbidden),
                    "update unavailable" => new(HttpStatusCode.ServiceUnavailable),
                    "invalid acknowledgement" => SettingsJson(InitialFlags),
                    _ => SettingsJson(updated)
                };
            }
            if (writes == 0) return SettingsJson(InitialFlags);
            return scenario == "readback unavailable" ? new(HttpStatusCode.ServiceUnavailable) :
                SettingsJson(scenario == "concurrent change" ? InitialFlags : updated);
        });
        using var work = new AdminSettingsWorkspace(new(http), session, "user-a", clock);
        await work.LoadSettingsAsync();
        await work.UpdateAsync(new(MaintenanceMode: true));
        Assert.Equal(1, writes);
        Assert.Null(work.Notice);
        Assert.NotNull(work.SettingsError);
        if (scenario == "concurrent change") Assert.Equal(InitialFlags, work.Settings);
        else if (scenario == "invalid acknowledgement") Assert.Equal(updated, work.Settings);
        else Assert.Null(work.Settings);
    }

    [Fact]
    public async Task SettingsListFailureDoesNotHideHealthAndRefreshRecovers()
    {
        await RestoreRole("admin");
        var reads = 0;
        using var http = HistoryHttp(req => req.RequestUri!.AbsolutePath == "/api/health"
            ? SettingsJson(new ApiHealthResponse(true, "Running")) : ++reads == 1
                ? SettingsJson(new { auto_approve_uploads = true }) : SettingsJson(InitialFlags));
        using var work = new AdminSettingsWorkspace(new(http), session, "user-a", clock);
        await Task.WhenAll(work.LoadSettingsAsync(), work.CheckHealthAsync());
        Assert.Null(work.Settings);
        Assert.NotNull(work.SettingsError);
        Assert.Equal("Healthy", work.Health?.Status);
        await work.LoadSettingsAsync();
        Assert.Equal(InitialFlags, work.Settings);
        Assert.Null(work.SettingsError);
    }

    private sealed class HealthTestClock : TimeProvider
    {
        public long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    [Theory]
    [InlineData("ok", "Healthy", true)]
    [InlineData("not ok", "Unhealthy", true)]
    [InlineData("503", "Unhealthy (HTTP 503)", true)]
    [InlineData("invalid", "Invalid health response", true)]
    [InlineData("network", "Unreachable", false)]
    [InlineData("timeout", "Timed out", false)]
    public async Task HealthReportsPublicEndpointResultAndMeasuredRoundTrip(string scenario, string expected, bool received)
    {
        await RestoreRole("superuser");
        var timer = new HealthTestClock();
        using var http = HistoryHttp(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/admin/settings") return SettingsJson(InitialFlags);
            Assert.Equal("/api/health", req.RequestUri.AbsolutePath);
            Assert.Null(req.Headers.Authorization);
            timer.Milliseconds += 37;
            return scenario switch
            {
                "503" => new(HttpStatusCode.ServiceUnavailable),
                "network" => throw new HttpRequestException(),
                "timeout" => throw new TaskCanceledException(),
                "invalid" => SettingsJson(new { message = "missing ok" }),
                _ => SettingsJson(new ApiHealthResponse(scenario == "ok", "Response"))
            };
        });
        using var work = new AdminSettingsWorkspace(new(http), session, "user-a", timer);
        await Task.WhenAll(work.LoadSettingsAsync(), work.CheckHealthAsync());
        Assert.Equal(expected, work.Health?.Status);
        Assert.Equal(received ? TimeSpan.FromMilliseconds(37) : (TimeSpan?)null, work.Health?.ResponseTime);
        Assert.Equal(InitialFlags, work.Settings);
    }

    [Fact]
    public async Task RoleLossDuringSaveRejectsLateResultAndOverlappingToggle()
    {
        await RestoreRole("admin");
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var reads = 0;
        using var http = new HttpClient(new HistoryHandler(req =>
        {
            if (req.Method == HttpMethod.Patch) { writes++; return held.Task; }
            reads++;
            return Task.FromResult(SettingsJson(InitialFlags));
        })) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var work = new AdminSettingsWorkspace(new(http), session, "user-a", clock);
        await work.LoadSettingsAsync();
        var change = work.UpdateAsync(new(MaintenanceMode: true));
        Assert.True(work.SettingsBusy);
        await work.UpdateAsync(new(AutoApproveUploads: true));
        backend.Profile = _ => Task.FromResult(ActiveProfile());
        await session.RestoreAsync();
        held.SetResult(SettingsJson(InitialFlags with { MaintenanceMode = true }));
        await change;
        Assert.Equal(1, writes);
        Assert.Equal(1, reads);
        Assert.Null(work.Settings);
        Assert.Null(work.Notice);
        Assert.NotNull(work.SettingsError);
    }
}

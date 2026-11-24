using System.Net;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Auth;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    private const string Gif = "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==";

    [Fact]
    public async Task SettingsRendersOnlyVerifiedProfileAndNewPasswordInputs()
    {
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with
            { FullName = "Pat Jones", Role = "superuser", AvatarUrl = "data:image/gif;base64," + Gif });
        await session.RestoreAsync();
        using var services = NavigationServices(session, new TestNavigation("settings"));
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<SessionRouteView>(RouteParameters(typeof(Settings)));
            var html = page.ToHtmlString();
            Assert.Contains("Pat Jones", html);
            Assert.Contains("user@example.test", html);
            Assert.Contains("superuser", html);
            Assert.Contains("data:image/gif;base64,", html);
            Assert.Contains("aria-current=\"page\"", html);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "autocomplete=\"new-password\"").Count);
            Assert.DoesNotContain("current-password", html);
            Assert.DoesNotContain(browser.Session!, html);
        });
    }

    [Fact]
    public async Task SettingsFormsDispatchProfileAndPasswordChangesIndependently()
    {
        var stored = ActiveProfile() with { FullName = "Original" };
        backend.Profile = _ => Task.FromResult(stored);
        backend.Update = (_, patch) => Task.FromResult(stored = stored with { FullName = patch.FullName });
        browser.Session = Token();
        await session.RestoreAsync();
        using var services = NavigationServices(session, new TestNavigation("settings"));
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Settings)));
            await page.QuiescenceTask;
            await renderer.InputAsync("Updated");
            await renderer.SubmitSettingsFormAsync(0);
            Assert.Contains("Profile saved.", page.ToHtmlString());
            Assert.Equal("Updated", stored.FullName);
            Assert.DoesNotContain("changePassword", browser.Calls);
            browser.PasswordOutcome = "mismatch";
            await renderer.SubmitSettingsFormAsync(1);
            Assert.Contains("do not match", page.ToHtmlString());
            browser.PasswordOutcome = "updated";
            await renderer.SubmitSettingsFormAsync(1);
            Assert.Contains("Password updated.", page.ToHtmlString());
        });
    }

    [Fact]
    public async Task DisabledProfileWritesNeverReachApi()
    {
        backend.Config = backend.Config with { ProfileWrites = "false" };
        backend.Update = (_, _) => throw new InvalidOperationException("Profile writes must be disabled.");
        browser.Session = Token();
        await session.RestoreAsync();
        var result = await session.SaveProfileAsync("user-a", "Pat", null, default);
        Assert.False(result.Success);
        Assert.Contains("disabled", result.Message);
    }

    [Fact]
    public async Task SavesNameAndAvatarThenRestoresThemInANewCircuit()
    {
        var stored = ActiveProfile();
        backend.Profile = _ => Task.FromResult(stored);
        browser.Session = Token();
        await session.RestoreAsync();
        var avatar = await ProfileAvatar.ReadAsync(new ImageFile("image/gif", Convert.FromBase64String(Gif)), default);
        backend.Update = (token, request) =>
        {
            Assert.Equal(browser.Session, token);
            Assert.Equal(new("Pat", "data:image/gif;base64," + Gif), request);
            stored = stored with { FullName = request.FullName, AvatarUrl = request.AvatarUrl };
            return Task.FromResult(stored with { Role = "admin" });
        };

        Assert.True((await session.SaveProfileAsync("user-a", " Pat ", avatar, default)).Success);
        Assert.Equal(stored, session.State.Profile);
        Assert.Equal("user", session.State.Profile!.Role);
        await using var reopened = new SupabaseSessionService(backend, browser, clock);
        await reopened.RestoreAsync();
        Assert.Equal(stored, reopened.State.Profile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Pat\nJones")]
    public async Task InvalidOrEmptyProfileEditsNeverReachBackend(string? name)
    {
        browser.Session = Token();
        await session.RestoreAsync();
        Assert.False((await session.SaveProfileAsync("user-a", name, null, default)).Success);
        Assert.Equal(ActiveProfile(), session.State.Profile);
    }

    [Theory]
    [InlineData(422)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task RejectedProfileWriteIsNotShownAsSavedOrReplayed(int code)
    {
        browser.Session = Token();
        await session.RestoreAsync();
        var writes = 0;
        backend.Update = (_, _) => { writes++; throw new HttpRequestException("private backend detail", null, (HttpStatusCode)code); };
        var result = await session.SaveProfileAsync("user-a", "Pat", null, default);
        Assert.False(result.Success);
        Assert.DoesNotContain("private backend detail", result.Message);
        Assert.Null(session.State.Profile?.FullName);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task IncorrectProfileResponseOrUnconfirmedReadNeverReportsSuccess()
    {
        browser.Session = Token();
        await session.RestoreAsync();
        backend.Update = (_, _) => Task.FromResult(ActiveProfile() with { Id = "other" });
        Assert.False((await session.SaveProfileAsync("user-a", "Pat", null, default)).Success);
        Assert.Equal(ActiveProfile(), session.State.Profile);
        backend.Update = (_, _) => Task.FromResult(ActiveProfile() with { FullName = "Pat" });
        Assert.False((await session.SaveProfileAsync("user-a", "Pat", null, default)).Success);
        backend.Profile = _ => throw new HttpRequestException();
        Assert.False((await session.SaveProfileAsync("user-a", "Pat", null, default)).Success);
        Assert.False(session.CanAccessChat);
    }

    [Fact]
    public async Task AccountSwitchDiscardsPendingWriteAndRejectsStaleOwnerActions()
    {
        var profile = ActiveProfile();
        backend.Profile = _ => Task.FromResult(profile);
        browser.Session = Token();
        await session.RestoreAsync();
        var held = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        backend.Update = (_, _) => { writes++; return held.Task; };
        var pending = session.SaveProfileAsync("user-a", "Old account name", null, default);
        Assert.False((await session.SaveProfileAsync("user-a", "Duplicate", null, default)).Success);
        profile = profile with { Id = "user-b", FullName = "Other person" };
        browser.Session = TestJwt(new { sub = "user-b", exp = clock.Now.AddHours(1).ToUnixTimeSeconds() });
        await session.BrowserSessionChanged("SIGNED_IN", "user-b");
        held.SetResult(ActiveProfile() with { FullName = "Old account name" });
        Assert.False((await pending).Success);
        Assert.False((await session.SaveProfileAsync("user-a", "Wrong", null, default)).Success);
        Assert.False((await session.ChangePasswordAsync("user-a", default, default, default)).Success);
        Assert.DoesNotContain("changePassword", browser.Calls);
        Assert.Equal(1, writes);
        Assert.Equal(profile, session.State.Profile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredSettingsSessionCannotChangeProfileOrPassword(bool password)
    {
        browser.Session = Token(minutes: 1);
        await session.RestoreAsync();
        clock.Now = clock.Now.AddMinutes(2);
        var result = password ? await session.ChangePasswordAsync("user-a", default, default, default)
            : await session.SaveProfileAsync("user-a", "Pat", null, default);
        Assert.False(result.Success);
        Assert.False(session.CanAccessChat);
        Assert.DoesNotContain("changePassword", browser.Calls);
    }

    [Theory]
    [InlineData("updated", true, "Password updated")]
    [InlineData("mismatch", false, "do not match")]
    [InlineData("weak_password", false, "longer")]
    [InlineData("reauthenticate", false, "Sign in again")]
    [InlineData("expired", false, "Sign in again")]
    [InlineData("unavailable", false, "could not confirm")]
    public async Task SettingsOnlyReportsConfirmedPasswordChanges(string outcome, bool success, string message)
    {
        browser.Session = Token();
        await session.RestoreAsync();
        browser.PasswordOutcome = outcome;
        var result = await session.ChangePasswordAsync("user-a", default, default, default);
        Assert.Equal(success, result.Success);
        Assert.Contains(message, result.Message);
        Assert.Single(browser.Calls, call => call == "changePassword");
        Assert.Equal(outcome == "expired" ? 2 : 1, backend.CheckedTokens.Count);
    }

    [Theory]
    [InlineData("image/svg+xml", 20, false)]
    [InlineData("image/jpeg", ProfileAvatar.MaxBytes + 1, false)]
    [InlineData("image/png", 0, false)]
    [InlineData("image/jpeg", 4, true)]
    public async Task RejectsUnsafeOrOversizedAvatarsBeforeEncoding(string mime, long size, bool opened)
    {
        var file = new ImageFile(mime, [1, 2, 3, 4], size);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileAvatar.ReadAsync(file, default));
        Assert.Equal(opened, file.Opened);
    }

    [Theory]
    [InlineData("data:image/svg+xml;base64,unused")]
    [InlineData("javascript:unused")]
    [InlineData("https://name@example.test/image.png")]
    public void UnsafeStoredAvatarIsHidden(string value) => Assert.Null(ProfileAvatar.DisplayUrl(value));

    private sealed class ImageFile(string type, byte[] bytes, long? declaredSize = null) : IBrowserFile
    {
        public string Name => "avatar";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => declaredSize ?? bytes.Length;
        public string ContentType => type;
        public bool Opened { get; private set; }
        public Stream OpenReadStream(long maxAllowedSize, CancellationToken cancellationToken)
        {
            Assert.Equal(ProfileAvatar.MaxBytes, maxAllowedSize);
            Opened = true;
            return new MemoryStream(bytes);
        }
    }
}

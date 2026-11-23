using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests : IAsyncDisposable
{
    private readonly TestBackend backend = new();
    private readonly TestBrowser browser = new();
    private readonly TestClock clock = new();
    private readonly SupabaseSessionService session;

    public SupabaseSessionTests() => session = new(backend, browser, clock);
    public ValueTask DisposeAsync() => session.DisposeAsync();

    [Fact]
    public async Task DirectChatPrerenderWithUnknownIdentityHidesTheChatComponent()
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton<NavigationManager, TestNavigation>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = new Dictionary<string, object?>
            {
                ["RouteData"] = new RouteData(typeof(Chat), new Dictionary<string, object?>())
            };
            var result = await renderer.RenderComponentAsync<SessionRouteView>(ParameterView.FromDictionary(parameters));
            return result.ToHtmlString();
        });
        Assert.Contains("Checking access", html);
        Assert.DoesNotContain("chat-composer", html);
        Assert.DoesNotContain("New Chat", html);
        Assert.Empty(browser.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoginRouteAcceptsAutofillAfterSessionCheckEvenWhenInitializationFailed(bool restore, bool sdkFails)
    {
        browser.InitializeFails = sdkFails;
        if (restore) await session.RestoreAsync();
        using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton<IJSRuntime>(browser)
            .AddSingleton<NavigationManager, TestNavigation>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = new Dictionary<string, object?>
            {
                ["RouteData"] = new RouteData(typeof(Login), new Dictionary<string, object?>())
            };
            var page = await renderer.RenderComponentAsync<SessionRouteView>(ParameterView.FromDictionary(parameters));
            return page.ToHtmlString();
        });
        Assert.Matches("<input[^>]*type=\"email\"[^>]*required", html);
        Assert.Matches("<input[^>]*type=\"password\"[^>]*required", html);
        Assert.Matches(restore ? "<button type=\"submit\">" : "<button type=\"submit\" disabled", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InteractiveLoginInitializesAndDispatchesFormSubmission(bool firstInitializationFails)
    {
        browser.InitializeFails = firstInitializationFails;
        var navigation = new TestNavigation("login");
        using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton<IJSRuntime>(browser).AddSingleton<NavigationManager>(navigation).BuildServiceProvider();
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["RouteData"] = new RouteData(typeof(Login), new Dictionary<string, object?>())
            });
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), parameters);
            await page.QuiescenceTask;
            Assert.Equal(firstInitializationFails ? new[] { "initialize" } : ["initialize", "restore"], browser.Calls);
            Assert.Contains("<button type=\"submit\">", page.ToHtmlString());

            browser.InitializeFails = false;
            browser.Error = "credentials";
            await renderer.SubmitAsync();
            Assert.Contains("Sign-in failed", page.ToHtmlString());
            Assert.False(session.CanAccessChat);

            browser.Error = null;
            browser.Session = Token();
            var verification = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            backend.Profile = _ => verification.Task;
            var pending = renderer.SubmitAsync();
            Assert.Contains("<button type=\"submit\" disabled", page.ToHtmlString());
            await renderer.SubmitAsync();
            Assert.Equal(2, browser.Calls.Count(call => call == "signIn"));
            Assert.EndsWith("/login", navigation.Uri);
            verification.SetResult(ActiveProfile());
            await pending;
            Assert.True(session.CanAccessChat);
            Assert.EndsWith("/chat", navigation.Uri);
        });
    }

    [Theory]
    [InlineData(false, "This email has not been invited.")]
    [InlineData(false, "Your invitation has expired.")]
    [InlineData(true, "")]
    public async Task SignupFormChecksTheInvitationBeforeShowingPasswords(bool eligible, string denial)
    {
        var check = new TaskCompletionSource<CheckEmailResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Check = _ => check.Task;
        using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton<IJSRuntime>(browser).AddSingleton<NavigationManager, TestNavigation>().BuildServiceProvider();
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["RouteData"] = new RouteData(typeof(Login), new Dictionary<string, object?>()) }));
            await page.QuiescenceTask;
            await renderer.ClickButtonAsync();
            await renderer.InputAsync("invalid");
            Assert.Contains("<button type=\"submit\" disabled", page.ToHtmlString());
            await renderer.SubmitAsync();
            Assert.Empty(backend.InvitationEmails);
            await renderer.InputAsync(" invitee@example.test ");
            Assert.Contains("<button type=\"submit\">", page.ToHtmlString());
            var pending = renderer.SubmitAsync();
            Assert.Contains("Checking invitation", page.ToHtmlString());
            await renderer.SubmitAsync();
            Assert.Equal(["invitee@example.test"], backend.InvitationEmails);
            check.SetResult(new(eligible, denial));
            await pending;

            if (!eligible)
            {
                Assert.Contains(denial, page.ToHtmlString());
                Assert.DoesNotContain("signup-password", page.ToHtmlString());
                Assert.DoesNotContain("signUp", browser.Calls);
                return;
            }
            Assert.Contains("signup-confirmation", page.ToHtmlString());
            browser.ConfirmationRequired = true;
            await renderer.SubmitAsync();
            Assert.Equal(["invitee@example.test", "invitee@example.test"], backend.InvitationEmails);
            Assert.Single(browser.Calls, call => call == "signUp");
            Assert.Contains("Check your inbox", page.ToHtmlString());
            Assert.Contains("login-password", page.ToHtmlString());
            Assert.False(session.CanAccessChat);
            Assert.Empty(backend.CheckedTokens);
        });
    }

    [Fact]
    public async Task RegistrationRechecksInvitationAndSuppressesOverlappingSubmissions()
    {
        await session.RestoreAsync();
        var check = new TaskCompletionSource<CheckEmailResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True((await session.CheckInvitationAsync("invitee@example.test")).Eligible);
        backend.Check = _ => check.Task;
        var pending = session.SignUpAsync("invitee@example.test", default, default);
        Assert.Equal(SignUpOutcome.Failed, (await session.SignUpAsync("invitee@example.test", default, default)).Outcome);
        check.SetResult(new(false, "Your invitation has expired."));
        Assert.Equal(SignUpOutcome.InvitationDenied, (await pending).Outcome);
        Assert.Equal(2, backend.InvitationEmails.Count);
        Assert.DoesNotContain("signUp", browser.Calls);
        backend.Check = _ => throw new HttpRequestException();
        Assert.Equal(SignUpOutcome.InvitationDenied, (await session.SignUpAsync("invitee@example.test", default, default)).Outcome);
        Assert.DoesNotContain("signUp", browser.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImmediateSignupSessionRequiresMatchingActiveProfile(bool active)
    {
        await session.RestoreAsync();
        browser.Session = Token();
        var verification = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Profile = _ => verification.Task;
        var attempt = session.SignUpAsync("invitee@example.test", default, default);
        Assert.False(session.CanAccessChat);
        Assert.Equal(browser.Session, Assert.Single(backend.CheckedTokens));
        var duplicate = await session.SignUpAsync("invitee@example.test", default, default);
        Assert.Equal(SignUpOutcome.Failed, duplicate.Outcome);
        Assert.Single(browser.Calls, call => call == "signUp");
        verification.SetResult(ActiveProfile() with { Status = active ? "active" : "inactive" });
        Assert.Equal(SignUpOutcome.SessionReceived, (await attempt).Outcome);
        Assert.Equal(active, session.CanAccessChat);
        Assert.Equal(active ? SessionStatus.Active : SessionStatus.SignedOut, session.State.Status);
    }

    [Theory]
    [InlineData("existing_account", "already exists")]
    [InlineData("weak_password", "stronger password")]
    [InlineData("mismatch", "do not match")]
    [InlineData("rate_limit", "Too many")]
    [InlineData("registration_failed", "Could not confirm")]
    public async Task SignupErrorsGiveSafeFeedbackWithoutGrantingAccess(string error, string expected)
    {
        await session.RestoreAsync();
        browser.Error = error;
        var result = await session.SignUpAsync("invitee@example.test", default, default);
        Assert.Equal(SignUpOutcome.Failed, result.Outcome);
        Assert.Contains(expected, result.Message);
        Assert.Empty(backend.CheckedTokens);
        Assert.False(session.CanAccessChat);
    }

    // Acknowledge the render batch as a browser would, then dispatch the form's actual handler ID.
#pragma warning disable BL0006
    private sealed class LoginRenderer : StaticHtmlRenderer
    {
        public LoginRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : base(services, loggerFactory)
        {
            ElementReferenceContext = new WebElementReferenceContext(services.GetRequiredService<IJSRuntime>());
        }

        private ulong submitHandler;
        private ulong buttonHandler, inputHandler, changeHandler;
        private ulong keyDownHandler, errorHandler;
        private int chatComponentId;
        private int settingsComponentId;

        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            foreach (var frame in batch.ReferenceFrames.Array.AsSpan(0, batch.ReferenceFrames.Count))
            {
                if (frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(Chat))
                    chatComponentId = frame.ComponentId;
                if (frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(Settings))
                    settingsComponentId = frame.ComponentId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onsubmit")
                    submitHandler = frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onclick")
                    buttonHandler = frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "oninput")
                    inputHandler = frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onchange")
                    changeHandler = frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onkeydown")
                    keyDownHandler = frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onerror")
                    errorHandler = frame.AttributeEventHandlerId;
            }
            return Task.CompletedTask;
        }

        public Task SubmitAsync() => DispatchEventAsync(submitHandler, null, EventArgs.Empty);
        public Task SubmitSettingsFormAsync(int index)
        {
            var frames = GetCurrentRenderTreeFrames(settingsComponentId);
            var handler = frames.Array.Take(frames.Count)
                .Where(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onsubmit")
                .ElementAt(index).AttributeEventHandlerId;
            return DispatchEventAsync(handler, null, EventArgs.Empty);
        }
        public Task ClickButtonAsync() => DispatchEventAsync(buttonHandler, null, new MouseEventArgs());
        public Task PressEnterAsync() => DispatchEventAsync(keyDownHandler, null, new KeyboardEventArgs { Key = "Enter" });
        public Task ImageErrorAsync() => DispatchEventAsync(errorHandler, null, new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());
        public Task InputAsync(string text) => DispatchEventAsync(inputHandler, null, new ChangeEventArgs { Value = text });
        public Task SelectAsync(string value) => DispatchEventAsync(changeHandler, null, new ChangeEventArgs { Value = value });

        public Task ClickChatButtonAsync(string label)
        {
            var frames = GetCurrentRenderTreeFrames(chatComponentId);
            for (var i = 0; i < frames.Count; i++)
            {
                var element = frames.Array[i];
                if (element.FrameType != RenderTreeFrameType.Element || element.ElementName != "button") continue;
                ulong handler = 0;
                foreach (var child in frames.Array.AsSpan(i + 1, element.ElementSubtreeLength - 1))
                {
                    if (child.FrameType == RenderTreeFrameType.Attribute && child.AttributeName == "onclick")
                        handler = child.AttributeEventHandlerId;
                    if (child.FrameType == RenderTreeFrameType.Text && child.TextContent.Trim() == label && handler != 0)
                        return DispatchEventAsync(handler, null, new MouseEventArgs());
                }
            }
            throw new InvalidOperationException($"Chat button '{label}' was not rendered.");
        }
    }
#pragma warning restore BL0006

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyVerifiedProfileGrantsAccessForSignInOrRestore(bool signIn)
    {
        browser.Session = Token();
        var response = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Profile = _ => response.Task;
        var attempt = signIn ? session.SignInAsync(default, default) : session.RestoreAsync();
        Assert.False(session.CanAccessChat);
        Assert.Equal(browser.Session, Assert.Single(backend.CheckedTokens));
        response.SetResult(ActiveProfile());
        await attempt;
        Assert.True(session.CanAccessChat);
        Assert.Contains(signIn ? "signIn" : "restore", browser.Calls);
        Assert.Equal("user-a", session.State.Profile!.Id);
    }

    [Fact]
    public async Task ExpiredSessionRefreshesOnceAndChecksNewToken()
    {
        browser.Session = Token(minutes: -1);
        browser.Refreshed = Token();
        await session.RestoreAsync();
        Assert.True(session.CanAccessChat);
        Assert.Equal(browser.Refreshed, Assert.Single(backend.CheckedTokens));
        Assert.Single(browser.Calls, call => call == "refresh");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task InactiveOrMissingProfileClearsSession(HttpStatusCode status)
    {
        browser.Session = Token();
        backend.Profile = _ => throw new HttpRequestException(null, null, status);
        await session.RestoreAsync();
        Assert.False(session.CanAccessChat);
        Assert.Null(browser.Session);
        Assert.Contains("contact", session.State.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("user-a", "inactive")]
    [InlineData("user-b", "active")]
    public async Task RejectsInactiveOrMismatchedSuccessfulProfile(string id, string status)
    {
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Id = id, Status = status });
        await session.RestoreAsync();
        Assert.False(session.CanAccessChat);
        Assert.Contains("signOut", browser.Calls);
    }

    [Fact]
    public async Task RepeatedUnauthorizedEndsSessionAfterOneRefresh()
    {
        browser.Session = Token();
        browser.Refreshed = Token(minutes: 15);
        backend.Profile = _ => throw new HttpRequestException(null, null, HttpStatusCode.Unauthorized);
        await session.RestoreAsync();
        Assert.Equal(SessionStatus.SignedOut, session.State.Status);
        Assert.Single(browser.Calls, call => call == "refresh");
        Assert.Equal(2, backend.CheckedTokens.Count);
        Assert.Null(browser.Session);
    }

    [Fact]
    public async Task ProfileOutageFailsClosedAndRetryUsesExistingBrowserSession()
    {
        browser.Session = Token();
        backend.Profile = _ => throw new HttpRequestException("internal detail", null, HttpStatusCode.ServiceUnavailable);
        await session.RestoreAsync();
        Assert.Equal(SessionStatus.Unavailable, session.State.Status);
        Assert.Contains("profile (HTTP 503)", session.State.Message);
        Assert.DoesNotContain("internal detail", session.State.Message);
        Assert.False(session.CanAccessChat);
        Assert.DoesNotContain("signOut", browser.Calls);
        backend.Profile = _ => Task.FromResult(ActiveProfile());
        await session.RestoreAsync();
        Assert.True(session.CanAccessChat);
    }

    [Fact]
    public async Task MissingSessionIsAnErrorOnlyAfterSubmittingCredentials()
    {
        await session.RestoreAsync();
        Assert.Null(session.State.Message);
        await session.SignInAsync(default, default);
        Assert.Contains("did not return a sign-in session", session.State.Message);
        Assert.False(session.CanAccessChat);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetFormSubmitsThePublicEmailRequestAndDisplaysTheOutcome(bool accepted)
    {
        using var http = new HttpClient(new ResetHandler(accepted)) { BaseAddress = new("https://backend.example.test") };
        using var services = new ServiceCollection().AddLogging().AddSingleton(new FastApiClient(http))
            .AddSingleton<IJSRuntime>(browser).BuildServiceProvider();
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(ResetPassword), ParameterView.Empty);
            await page.QuiescenceTask;
            Assert.DoesNotContain("Set New Password", page.ToHtmlString());
            await renderer.SubmitAsync();
            Assert.Contains(accepted ? "If an account exists" : "Too many reset attempts", page.ToHtmlString());
        });
    }

    private sealed class ResetHandler(bool accepted) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/auth/forgot-password", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("""{"email":"user@example.test"}""", await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new ForgotPasswordResponse(accepted,
                    accepted ? "Request accepted" : "Too many reset attempts"))
            };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetPageRequiresVerifiedRecoveryBeforeSubmittingNewPassword(bool verified)
    {
        browser.HasRecoveryLink = true;
        browser.RecoveryVerified = verified;
        using var http = new HttpClient(new AuthConfigHandler(backend.Config)) { BaseAddress = new("https://backend.example.test") };
        using var services = new ServiceCollection().AddLogging().AddSingleton(new FastApiClient(http))
            .AddSingleton<IJSRuntime>(browser).BuildServiceProvider();
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(ResetPassword), ParameterView.Empty);
            await page.QuiescenceTask;
            Assert.Equal(["hasRecoveryLink", "prepareRecovery"], browser.Calls);
            if (verified)
            {
                Assert.Contains("Set New Password", page.ToHtmlString());
                await renderer.SubmitAsync();
                Assert.Contains("Password updated", page.ToHtmlString());
                Assert.Contains("Back to Sign In", page.ToHtmlString());
            }
            else
            {
                Assert.Contains("invalid or expired", page.ToHtmlString());
                Assert.DoesNotContain("Set New Password", page.ToHtmlString());
            }
            Assert.False(session.CanAccessChat);
        });
    }

    private sealed class AuthConfigHandler(AuthConfigResponse config) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/auth/config", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = System.Net.Http.Json.JsonContent.Create(config) });
        }
    }

    [Fact]
    public async Task SdkRefreshVerifiesReplacementAndExpiryBoundsCachedAccess()
    {
        browser.Session = Token(minutes: 1);
        await session.RestoreAsync();
        clock.Now = clock.Now.AddMinutes(2);
        Assert.False(session.CanAccessChat);
        browser.Session = Token(minutes: 20);
        await session.BrowserSessionChanged("TOKEN_REFRESHED", "user-a");
        Assert.True(session.CanAccessChat);
        Assert.Equal(browser.Session, backend.CheckedTokens.Last());
    }

    [Fact]
    public async Task SignOutInvalidatesInFlightVerificationAndIsCircuitScoped()
    {
        var otherBrowser = new TestBrowser { Session = Token() };
        await using var other = new SupabaseSessionService(new TestBackend(), otherBrowser, clock);
        await other.RestoreAsync();

        browser.Session = Token();
        var late = new TaskCompletionSource<ProfileResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Profile = _ => late.Task;
        var verification = session.RestoreAsync();
        await session.SignOutAsync();
        late.SetResult(ActiveProfile());
        await verification;
        Assert.False(session.CanAccessChat);
        Assert.Null(session.State.Profile);
        Assert.Null(browser.Session);
        Assert.True(other.CanAccessChat);
    }

    [Fact]
    public async Task DisconnectedSignOutBlocksRestoreUntilRetried()
    {
        browser.Session = Token();
        await session.RestoreAsync();
        browser.Disconnected = true;
        await session.SignOutAsync();
        Assert.False(session.CanAccessChat);
        Assert.Equal(SessionStatus.SignOutPending, session.State.Status);
        await session.BrowserSessionChanged("SIGNED_IN", "user-a");
        await session.RestoreAsync();
        Assert.Single(backend.CheckedTokens);
        browser.Disconnected = false;
        await session.SignOutAsync();
        Assert.Null(browser.Session);
        Assert.Equal(SessionStatus.SignedOut, session.State.Status);
    }

    [Theory]
    [InlineData("credentials", SessionStatus.SignedOut)]
    [InlineData("expired", SessionStatus.SignedOut)]
    [InlineData("unavailable", SessionStatus.Unavailable)]
    public async Task BrowserErrorsProvideFeedbackAndFailClosed(string code, SessionStatus expected)
    {
        browser.Error = code;
        await session.SignInAsync(default, default);
        Assert.Equal(expected, session.State.Status);
        Assert.NotNull(session.State.Message);
        Assert.Empty(backend.CheckedTokens);
    }

    [Fact]
    public async Task SubmitRetriesBrowserInitializationAfterOutageAndStillRequiresAnActiveProfile()
    {
        browser.InitializeFails = true;
        await session.RestoreAsync();
        Assert.Equal(SessionStatus.Unavailable, session.State.Status);
        Assert.Contains("browser", session.State.Message);
        Assert.False(session.BrowserInitialized);

        browser.InitializeFails = false;
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Status = "inactive" });
        await session.SignInAsync(default, default);
        Assert.Equal(["initialize", "initialize", "signIn", "signOut"], browser.Calls);
        Assert.Single(backend.CheckedTokens);
        Assert.False(session.CanAccessChat);
        Assert.Equal(SessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task ElevatedLegacyKeyNeverReachesBrowser()
    {
        backend.Config = backend.Config with { SupabaseKey = TestJwt(new { role = "service_role" }) };
        await session.RestoreAsync();
        Assert.Equal(SessionStatus.Unavailable, session.State.Status);
        Assert.Empty(browser.Calls);
        backend.Config = backend.Config with { SupabaseKey = TestJwt(new { role = "anon" }) };
        await session.RestoreAsync();
        Assert.Equal(SessionStatus.SignedOut, session.State.Status);
        Assert.Contains("initialize", browser.Calls);
    }

    [Fact]
    public async Task ChatUsesVerifiedTokenAndRefreshesExpiredSessionBeforeDispatch()
    {
        browser.Session = Token(minutes: 1);
        await session.RestoreAsync();
        var original = await session.WithAccessTokenAsync("user-a", (token, _) => Task.FromResult(token), default);
        Assert.Equal(browser.Session, original);
        clock.Now = clock.Now.AddMinutes(2);
        browser.Refreshed = Token();
        var current = await session.WithAccessTokenAsync("user-a", (token, _) => Task.FromResult(token), default);
        Assert.Equal(browser.Refreshed, current);
        Assert.NotEqual(original, current);
        Assert.Equal(current, backend.CheckedTokens.Last());
    }

    [Fact]
    public async Task ChatDenialRefreshesIdentityWithoutReplayingAWrite()
    {
        browser.Session = Token();
        browser.Refreshed = Token(minutes: 30);
        await session.RestoreAsync();
        var dispatches = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => session.WithAccessTokenAsync<int>("user-a", (_, _) =>
        {
            dispatches++;
            throw new HttpRequestException(null, null, HttpStatusCode.Unauthorized);
        }, default));
        Assert.Equal(1, dispatches);
        Assert.Equal(browser.Refreshed, backend.CheckedTokens.Last());
    }

    [Fact]
    public async Task ChatRejectsWrongOwnerAndDiscardsResponseAfterSignOut()
    {
        browser.Session = Token();
        await session.RestoreAsync();
        var response = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = session.WithAccessTokenAsync("user-a", (_, _) => response.Task, default);
        await Assert.ThrowsAsync<HttpRequestException>(() => session.WithAccessTokenAsync<int>("user-b",
            (_, _) => throw new InvalidOperationException("Wrong owner reached transport."), default));
        await session.SignOutAsync();
        response.SetResult(1);
        await Assert.ThrowsAsync<HttpRequestException>(() => pending);
        await Assert.ThrowsAsync<HttpRequestException>(() => session.WithAccessTokenAsync<int>("user-a",
            (_, _) => throw new InvalidOperationException("Signed-out request reached transport."), default));
    }

    private string Token(int minutes = 10) =>
        TestJwt(new { sub = "user-a", exp = clock.Now.AddMinutes(minutes).ToUnixTimeSeconds() });

    private static string TestJwt(object payload) =>
        "test." + WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload)) + ".unsigned";

    private static ProfileResponse ActiveProfile() =>
        new("user-a", "user@example.test", "user", "active", "2026-01-01T00:00:00Z");

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() : this("chat") { }
        public TestNavigation(string page) => Initialize("https://example.test/", "https://example.test/" + page);
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class TestBackend : IAuthenticationBackend
    {
        public AuthConfigResponse Config = new("https://auth.example.test", "sb_publishable_" + Guid.NewGuid());
        public Func<string, Task<ProfileResponse>> Profile = _ => Task.FromResult(ActiveProfile());
        public Func<string, UpdateProfileRequest, Task<ProfileResponse>> Update = (_, _) =>
            throw new InvalidOperationException("Unexpected profile write.");
        public Task<ProfileResponse> UpdateProfileAsync(string token, UpdateProfileRequest request, CancellationToken ct) =>
            Update(token, request);
        public List<string> CheckedTokens { get; } = [];
        public List<string> InvitationEmails { get; } = [];
        public Func<string, Task<CheckEmailResponse>> Check = _ => Task.FromResult(new CheckEmailResponse(true, "Eligible"));
        public Task<CheckEmailResponse> CheckEmailAsync(EmailRequest request, CancellationToken ct)
        {
            InvitationEmails.Add(request.Email);
            return Check(request.Email);
        }
        public Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken ct) => Task.FromResult(Config);
        public Task<ProfileResponse> GetProfileAsync(string token, CancellationToken ct)
        {
            CheckedTokens.Add(token);
            return Profile(token);
        }
    }

    private sealed class TestBrowser : IJSRuntime
    {
        public string? Session, Refreshed, Error;
        public bool Disconnected;
        public bool InitializeFails;
        public bool HasRecoveryLink, RecoveryVerified;
        public bool ConfirmationRequired;
        public string PasswordOutcome = "updated";
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string method, object?[]? args) => InvokeAsync<T>(method, default, args);
        public ValueTask<T> InvokeAsync<T>(string method, CancellationToken ct, object?[]? args)
        {
            if (Disconnected) throw new JSDisconnectedException("Circuit unavailable.");
            if (method is "Blazor._internal.domWrapper.focusBySelector" or "Blazor._internal.InputFile.init")
                return ValueTask.FromResult(default(T)!);
            var action = method.Replace("cfcAuth.", "", StringComparison.Ordinal);
            Calls.Add(action);
            if (action == "initialize" && InitializeFails) throw new JSException("SDK unavailable.");
            if (action == "refresh") Session = Refreshed;
            if (action == "signOut") Session = null;
            object? result = action switch
            {
                "hasRecoveryLink" => HasRecoveryLink,
                "prepareRecovery" => RecoveryVerified,
                "discardRecovery" => null,
                "updatePassword" when RecoveryVerified && args is [ElementReference, ElementReference] => "updated",
                "changePassword" when args is ["user-a", Guid, ElementReference, ElementReference] => PasswordOutcome,
                "readEmail" => "user@example.test",
                "signUp" when args is [string, ElementReference, ElementReference] =>
                    new SupabaseSessionService.BrowserSession { AccessToken = Session, Error = Error, ConfirmationRequired = ConfirmationRequired },
                "restore" or "refresh" or "signIn" => new SupabaseSessionService.BrowserSession { AccessToken = Session, Error = Error },
                "signOut" => true,
                "initialize" or "disconnect" => null,
                _ => throw new InvalidOperationException("Unexpected JS invocation.")
            };
            return ValueTask.FromResult((T)result!);
        }
    }
}

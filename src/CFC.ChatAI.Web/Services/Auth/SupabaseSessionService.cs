using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services.Auth;

public enum SessionStatus { Checking, SignedOut, Active, Unavailable, SignOutPending }

public sealed record SessionState(SessionStatus Status, string? Message = null, ProfileResponse? Profile = null);

public enum SignUpOutcome { Failed, InvitationDenied, ConfirmationRequired, SessionReceived }
public sealed record SignUpResult(SignUpOutcome Outcome, string? Message = null);

public sealed partial class SupabaseSessionService(IAuthenticationBackend backend, IJSRuntime js, TimeProvider clock)
    : IChatSessionAccess, IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Guid connectionId = Guid.NewGuid();
    private DotNetObjectReference<SupabaseSessionService>? receiver;
    private CancellationTokenSource? nextCheck;
    private DateTimeOffset expiresAt;
    private string? verifiedToken;
    private long revision;
    private bool initialized;
    private bool signingOut;
    private bool signingUp;
    private bool disposed;

    public SessionState State { get; private set; } = new(SessionStatus.Checking);
    public bool BrowserInitialized => initialized;
    internal bool ProfileWritesEnabled { get; private set; }
    internal bool AccountLifecycleEnabled { get; private set; }
    internal bool PermanentAccountDeletionEnabled { get; private set; }
    internal bool DocumentUploadsEnabled { get; private set; }
    public bool CanAccessChat => State.Status == SessionStatus.Active && clock.GetUtcNow() < expiresAt;
    public event Action? Changed;

    public Task RestoreAsync() => ReadAsync("restore", []);

    public async Task<T> WithAccessTokenAsync<T>(string ownerId, Func<string, CancellationToken, Task<T>> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!disposed && State.Status == SessionStatus.Active && State.Profile?.Id == ownerId && !CanAccessChat)
            await RestoreAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var token = verifiedToken;
        if (disposed || token is null || !CanAccessChat || State.Profile?.Id != ownerId)
            throw new HttpRequestException("Chat requires an active session.", null, HttpStatusCode.Unauthorized);

        try
        {
            var result = await request(token, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed || !CanAccessChat || State.Profile?.Id != ownerId)
                throw new HttpRequestException("The chat session changed.", null, HttpStatusCode.Unauthorized);
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Revalidate the same circuit. The caller decides how to reconcile a failed write.
            if (!disposed && !cancellationToken.IsCancellationRequested && verifiedToken == token && State.Profile?.Id == ownerId)
                await ReadAsync(ex.StatusCode == HttpStatusCode.Unauthorized ? "refresh" : "restore", []);
            throw;
        }
    }

    public Task SignInAsync(ElementReference emailInput, ElementReference passwordInput) =>
        ReadAsync("signIn", [emailInput, passwordInput]);

    public async Task<CheckEmailResponse> CheckInvitationAsync(string email)
    {
        try { return await backend.CheckEmailAsync(new(email.Trim()), lifetime.Token); }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            return new(false, "Could not check the invitation. Check your connection and try again.");
        }
    }

    public async Task<SignUpResult> SignUpAsync(string invitedEmail, ElementReference password, ElementReference confirmation)
    {
        if (disposed || signingOut || signingUp || !initialized || State.Status != SessionStatus.SignedOut)
            return new(SignUpOutcome.Failed, "Registration is unavailable while another session operation is in progress.");

        signingUp = true;
        var operation = ++revision;
        try
        {
            // The invitation can expire or be consumed while the password form is open.
            var email = invitedEmail.Trim();
            var invitation = await CheckInvitationAsync(email);
            if (!Current(operation)) return new(SignUpOutcome.Failed, "Your session changed. Please try again.");
            if (!invitation.Eligible) return new(SignUpOutcome.InvitationDenied, invitation.Message);

            var result = await js.InvokeAsync<BrowserSession>("cfcAuth.signUp", lifetime.Token, email, password, confirmation);
            if (!Current(operation)) return new(SignUpOutcome.Failed, "Your session changed. Please try signing in.");
            if (result.Error is not null)
            {
                var message = result.Error switch
                {
                    "short" => "Enter a password of at least 6 characters.",
                    "mismatch" => "Passwords do not match.",
                    "weak_password" => "Choose a stronger password and try again.",
                    "existing_account" => "An account already exists for this email. Sign in or reset your password.",
                    "rate_limit" => "Too many registration attempts. Please try again later.",
                    "signup_disabled" => "Registration is disabled. Contact your administrator.",
                    _ => "Could not confirm registration. Check your inbox or try signing in before retrying."
                };
                return new(SignUpOutcome.Failed, message);
            }
            if (!string.IsNullOrEmpty(result.AccessToken))
            {
                await VerifyAsync(result, operation, allowRefresh: true);
                return new(SignUpOutcome.SessionReceived);
            }
            if (result.ConfirmationRequired)
            {
                SetState(new(SessionStatus.SignedOut));
                return new(SignUpOutcome.ConfirmationRequired,
                    "Check your inbox for a confirmation link, then sign in. If you already have an account, sign in or reset your password.");
            }
            return new(SignUpOutcome.Failed, "Supabase did not return a registration result. Please try signing in.");
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            if (Current(operation) && State.Status == SessionStatus.Checking) SetUnavailable();
            return new(SignUpOutcome.Failed, "Could not confirm registration. Check your inbox or try signing in before retrying.");
        }
        finally { signingUp = false; }
    }

    [JSInvokable]
    public Task BrowserSessionChanged(string eventName, string? userId)
    {
        if (disposed || signingOut || State.Status == SessionStatus.SignOutPending)
            return Task.CompletedTask;

        // An event can revoke access immediately. Only the backend can grant it.
        if (eventName == "SIGNED_OUT")
        {
            revision++;
            SetState(new(SessionStatus.SignedOut, "Your session ended. Please sign in again."));
            return Task.CompletedTask;
        }
        if (State.Profile?.Id != userId)
            SetState(new(SessionStatus.Checking));
        return RestoreAsync();
    }

    private async Task ReadAsync(string action, object?[] arguments)
    {
        if (disposed || signingOut || State.Status == SessionStatus.SignOutPending) return;
        var operation = ++revision;
        if (!CanAccessChat) SetState(new(SessionStatus.Checking));
        try
        {
            if (!initialized)
            {
                var config = await backend.GetAuthConfigAsync(lifetime.Token);
                if (!Current(operation)) return;
                if (!IsPublicConfig(config))
                {
                    SetState(new(SessionStatus.Unavailable, "Browser sign-in is not configured. Contact your administrator."));
                    return;
                }
                receiver ??= DotNetObjectReference.Create(this);
                try
                {
                    await js.InvokeVoidAsync("cfcAuth.initialize", lifetime.Token,
                        config.SupabaseUrl, config.SupabaseKey, receiver, connectionId);
                }
                catch (JSException)
                {
                    if (Current(operation)) SetState(new(SessionStatus.Unavailable,
                        "The browser could not initialize Supabase sign-in. Check your connection and retry."));
                    return;
                }
                if (!Current(operation)) return;
                ProfileWritesEnabled = config is { Backend: "dotnet", ProfileWrites: "true" };
                AccountLifecycleEnabled = config is { Backend: "dotnet", AccountLifecycle: "true" };
                PermanentAccountDeletionEnabled = AccountLifecycleEnabled && config.PermanentAccountDeletion == "true";
                DocumentUploadsEnabled = config is { Backend: "dotnet", DocumentUploads: "true" };
                initialized = true;
            }

            var session = await js.InvokeAsync<BrowserSession>("cfcAuth." + action, lifetime.Token, arguments);
            if (!Current(operation)) return;
            if (action == "signIn" && session.Error == "unavailable")
                SetState(new(SessionStatus.Unavailable, "Supabase could not complete sign-in. Check your connection and retry."));
            else if (action == "signIn" && session.Error is null && string.IsNullOrEmpty(session.AccessToken))
                SetState(new(SessionStatus.SignedOut, "Supabase did not return a sign-in session. Please try again."));
            else await VerifyAsync(session, operation, allowRefresh: true);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            if (Current(operation))
            {
                if (!initialized) SetState(new(SessionStatus.Unavailable, "Could not load sign-in configuration from the API. Check the backend connection and retry."));
                else SetUnavailable();
            }
        }
    }

    private async Task VerifyAsync(BrowserSession session, long operation, bool allowRefresh)
    {
        if (session.Error == "expired")
        {
            await EndSessionAsync("Your session expired. Please sign in again.");
            return;
        }
        if (session.Error is "credentials" or "rate_limit")
        {
            SetState(new(SessionStatus.SignedOut, session.Error == "credentials"
                ? "Sign-in failed. Check your email and password and confirm your email address."
                : "Too many sign-in attempts. Please try again later."));
            return;
        }
        if (session.Error is not null) { SetUnavailable(); return; }
        if (string.IsNullOrEmpty(session.AccessToken))
        {
            SetState(new(SessionStatus.SignedOut, allowRefresh ? null : "Your session expired. Please sign in again."));
            return;
        }

        // These claims only identify a change of account and bound the lifetime of a verified profile.
        // The profile endpoint authenticates the exact token before the circuit grants access.
        using var claims = ReadJwtPayload(session.AccessToken);
        var userId = StringClaim(claims, "sub");
        var expiration = DateTimeOffset.MinValue;
        if (claims?.RootElement.TryGetProperty("exp", out var exp) == true &&
            exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds) &&
            seconds is >= 0 and <= 253402300799)
            expiration = DateTimeOffset.FromUnixTimeSeconds(seconds);

        if (State.Profile?.Id != userId || !CanAccessChat)
            SetState(new(SessionStatus.Checking));

        if (!string.IsNullOrEmpty(userId) && expiration > clock.GetUtcNow())
        {
            try
            {
                var profile = await backend.GetProfileAsync(session.AccessToken, lifetime.Token);
                if (!Current(operation)) return;
                if (profile.Status != "active" || profile.Id != userId)
                {
                    await EndSessionAsync("This account cannot access chat. Contact your administrator.");
                    return;
                }
                if (expiration > clock.GetUtcNow())
                {
                    expiresAt = expiration;
                    verifiedToken = session.AccessToken;
                    SetState(new(SessionStatus.Active, Profile: profile));
                    ScheduleCheck(expiration - clock.GetUtcNow());
                    return;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                if (Current(operation)) await EndSessionAsync("This account cannot access chat. Contact your administrator.");
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!Current(operation)) return;
            }
            catch (Exception ex) when (IsServiceFailure(ex))
            {
                var status = ex is HttpRequestException { StatusCode: { } code } ? $" (HTTP {(int)code})" : "";
                if (Current(operation)) SetState(new(SessionStatus.Unavailable,
                    $"The API could not verify your profile{status}. Retry the connection or contact your administrator."));
                return;
            }
        }

        SetState(new(SessionStatus.Checking));
        if (allowRefresh)
        {
            var refreshed = await js.InvokeAsync<BrowserSession>("cfcAuth.refresh", lifetime.Token);
            if (Current(operation)) await VerifyAsync(refreshed, operation, allowRefresh: false);
        }
        else await EndSessionAsync("Your session expired. Please sign in again.");
    }

    public Task SignOutAsync() => EndSessionAsync(null);

    private async Task EndSessionAsync(string? message)
    {
        if (disposed || signingOut) return;
        var operation = ++revision;
        signingOut = true;
        SetState(new(SessionStatus.Checking));
        try
        {
            var revoked = await js.InvokeAsync<bool>("cfcAuth.signOut", lifetime.Token);
            if (Current(operation)) SetState(new(SessionStatus.SignedOut, message ?? (revoked ? null :
                "Signed out in this tab. The authentication server could not confirm revocation.")));
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            if (Current(operation)) SetState(new(SessionStatus.SignOutPending,
                "Browser sign-out could not be confirmed. Reconnect and retry sign-out."));
        }
        finally { signingOut = false; }
    }

    private void ScheduleCheck(TimeSpan remaining)
    {
        nextCheck?.Cancel();
        nextCheck?.Dispose();
        nextCheck = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var delay = TimeSpan.FromSeconds(Math.Clamp(remaining.TotalSeconds, 0, 120));
        _ = CheckLaterAsync(delay, nextCheck.Token);
    }

    private async Task CheckLaterAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, clock, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) await RestoreAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void SetUnavailable() => SetState(new(SessionStatus.Unavailable,
        "Could not verify your session. Check your connection and try again."));

    private void SetState(SessionState state)
    {
        if (state.Status != SessionStatus.Active)
        {
            verifiedToken = null;
            nextCheck?.Cancel();
        }
        if (State == state) return;
        State = state;
        Changed?.Invoke();
    }

    private bool Current(long operation) => !disposed && revision == operation;
    private static bool IsServiceFailure(Exception ex) =>
        ex is HttpRequestException or JSException or JSDisconnectedException or OperationCanceledException or JsonException;

    internal static bool IsPublicConfig(AuthConfigResponse config)
    {
        if (!Uri.TryCreate(config.SupabaseUrl, UriKind.Absolute, out var url) ||
            !(url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback)) ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(config.SupabaseKey)) return false;

        if (config.SupabaseKey.StartsWith("sb_publishable_", StringComparison.Ordinal)) return true;
        using var claims = ReadJwtPayload(config.SupabaseKey);
        return StringClaim(claims, "role") == "anon";
    }

    private static JsonDocument? ReadJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            if (payload.RootElement.ValueKind == JsonValueKind.Object) return payload;
            payload.Dispose();
        }
        catch (FormatException) { }
        catch (JsonException) { }
        return null;
    }

    private static string? StringClaim(JsonDocument? claims, string name) =>
        claims?.RootElement.TryGetProperty(name, out var value) == true && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        verifiedToken = null;
        lifetime.Cancel();
        nextCheck?.Dispose();
        try
        {
            if (receiver is not null) await js.InvokeVoidAsync("cfcAuth.disconnect", connectionId);
        }
        catch (Exception ex) when (IsServiceFailure(ex) || ex is InvalidOperationException) { }
        receiver?.Dispose();
        lifetime.Dispose();
    }

    public sealed class BrowserSession
    {
        public string? AccessToken { get; init; }
        public string? Error { get; init; }
        public bool ConfirmationRequired { get; init; }
    }
}

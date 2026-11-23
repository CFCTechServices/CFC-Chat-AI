using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services;

public enum SessionStatus { Checking, SignedOut, Active, Unavailable, SignOutPending }

public sealed record SessionState(SessionStatus Status, string? Message = null, ProfileResponse? Profile = null);

public sealed class SupabaseSessionService(IAuthenticationBackend backend, IJSRuntime js, TimeProvider clock)
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
    private bool disposed;

    public SessionState State { get; private set; } = new(SessionStatus.Checking);
    public bool BrowserInitialized => initialized;
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
                if (!initialized) SetState(new(SessionStatus.Unavailable, "Could not load sign-in configuration from FastAPI. Check the backend connection and retry."));
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
                    $"FastAPI could not verify your profile{status}. Retry the connection or contact your administrator."));
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
    }
}

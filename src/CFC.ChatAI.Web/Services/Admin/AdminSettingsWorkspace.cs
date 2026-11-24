using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Api;
using CFC.ChatAI.Web.Services.Auth;

namespace CFC.ChatAI.Web.Services.Admin;

public sealed record ApiHealthCheck(string Status, TimeSpan? ResponseTime = null, string? Version = null);

public sealed class AdminSettingsWorkspace(ChatApiClient api, SupabaseSessionService session, string ownerId,
    TimeProvider clock) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    public AdminSystemSettings? Settings { get; private set; }
    public ApiHealthCheck? Health { get; private set; }
    public bool SettingsBusy { get; private set; }
    public bool HealthBusy { get; private set; }
    public string? SettingsError { get; private set; }
    public string? Notice { get; private set; }
    private bool Authorized => !lifetime.IsCancellationRequested && session.HasAdminAccess(ownerId);

    public Task LoadSettingsAsync() => ReadOrUpdateAsync(null);
    public Task UpdateAsync(AdminSettingsPatch patch) => Settings is null || patch.Matches(Settings)
        ? Task.CompletedTask : ReadOrUpdateAsync(patch);

    private async Task ReadOrUpdateAsync(AdminSettingsPatch? patch)
    {
        if (SettingsBusy || lifetime.IsCancellationRequested) return;
        var ct = lifetime.Token;
        SettingsBusy = true;
        SettingsError = Notice = null;
        bool patchAcknowledged = false;
        try
        {
            if (patch is not null)
            {
                var updated = await session.WithAdminTokenAsync(ownerId,
                    (token, cancel) => api.UpdateAdminSettingsAsync(token, patch, cancel), ct);
                patchAcknowledged = patch.Matches(updated);
            }
            var saved = await session.WithAdminTokenAsync(ownerId, api.GetAdminSettingsAsync, ct);
            ct.ThrowIfCancellationRequested();
            Settings = saved;
            if (patch is not null)
            {
                if (patchAcknowledged && patch.Matches(saved)) Notice = "Setting saved.";
                else SettingsError = "The change was not confirmed. The current configuration is shown.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            Settings = null;
            if (!Authorized || ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                SettingsError = "Administrator access could not be verified. Refresh or sign in again.";
            else if (patchAcknowledged) SettingsError = "The API accepted the change, but the saved configuration could not be read. Refresh to check it.";
            else if (patch is not null) SettingsError = "Could not confirm the change. Refresh settings before trying again.";
            else SettingsError = "Could not load settings from the API. Refresh to try again.";
        }
        finally { SettingsBusy = false; }
    }

    public async Task CheckHealthAsync()
    {
        if (HealthBusy || lifetime.IsCancellationRequested) return;
        Health = null;
        if (!Authorized) return;
        var ct = lifetime.Token;
        HealthBusy = true;
        var started = clock.GetTimestamp();
        ApiHealthCheck result;
        try
        {
            // Measure the public endpoint from the Blazor server. It needs no user credentials.
            var response = await api.GetHealthAsync(ct);
            result = new(response.Ok ? "Healthy" : "Unhealthy", clock.GetElapsedTime(started), response.Version);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status)
        {
            result = new($"Unhealthy (HTTP {(int)status})", clock.GetElapsedTime(started));
        }
        catch (JsonException) { result = new("Invalid health response", clock.GetElapsedTime(started)); }
        catch (HttpRequestException) { result = new("Unreachable"); }
        catch (OperationCanceledException) { result = new("Timed out"); }
        finally { HealthBusy = false; }
        if (Authorized) Health = result;
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

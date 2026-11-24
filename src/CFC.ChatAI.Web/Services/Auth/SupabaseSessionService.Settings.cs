using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services.Auth;

public sealed record SettingsResult(bool Success, string Message);

public sealed partial class SupabaseSessionService
{
    private bool savingProfile;

    public async Task<SettingsResult> SaveProfileAsync(string ownerId, string? changedName, ProfileAvatar? avatar,
        CancellationToken cancellationToken)
    {
        if (!ProfileWritesEnabled) return new(false, "Profile editing is disabled on this deployment.");
        var name = changedName?.Trim();
        if (name is not null && (name.Length == 0 || name.Any(char.IsControl)))
            return new(false, "Enter a full name without control characters.");
        if (name is null && avatar is null) return new(false, "Choose a profile change to save.");
        if (savingProfile) return new(false, "A profile update is already in progress.");
        savingProfile = true;
        try
        {
            var updated = await WithAccessTokenAsync(ownerId, (token, ct) =>
                backend.UpdateProfileAsync(token, new(name, avatar?.DataUrl), ct), cancellationToken);
            // A fresh verified GET also supersedes any profile read started before the write.
            await RestoreAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var profile = State.Profile;
            if (!CanAccessChat || profile?.Id != ownerId || updated.Id != ownerId || updated.Status != "active" ||
                (name is not null && profile.FullName != name) || (avatar is not null && profile.AvatarUrl != avatar.DataUrl))
                return new(false, "Could not confirm the saved profile. Refresh Settings before trying again.");
            return new(true, "Profile saved.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            return new(false, "Your profile change was rejected. Check the name and image, then try again.");
        }
        catch (Exception ex) when (IsServiceFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            return new(false, ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized }
                ? "Your session could not authorize this change. Sign in again."
                : "Could not confirm the profile update. Check your connection and refresh Settings.");
        }
        finally { savingProfile = false; }
    }

    public async Task<SettingsResult> ChangePasswordAsync(string ownerId, ElementReference password,
        ElementReference confirmation, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await WithAccessTokenAsync(ownerId, (_, ct) => js.InvokeAsync<string>(
                "cfcAuth.changePassword", ct, ownerId, connectionId, password, confirmation).AsTask(), cancellationToken);
            if (outcome is "expired" or "changed") await RestoreAsync();
            return outcome switch
            {
                "updated" => new(true, "Password updated."),
                "short" => new(false, "Enter at least 6 characters for your new password."),
                "mismatch" => new(false, "The two new passwords do not match."),
                "weak_password" => new(false, "Choose a longer password or add a mix of letters, numbers and symbols."),
                "same_password" => new(false, "Choose a password different from your current one."),
                "reauthenticate" or "expired" or "changed" => new(false, "Sign in again before changing your password."),
                "rate_limit" => new(false, "Too many attempts. Please try again later."),
                "busy" => new(false, "A password change is already in progress."),
                _ => new(false, "Supabase could not confirm the password change. Check your connection and try signing in.")
            };
        }
        catch (Exception ex) when (IsServiceFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            return new(false, "Could not confirm the password change. Check your connection and sign in again.");
        }
    }
}

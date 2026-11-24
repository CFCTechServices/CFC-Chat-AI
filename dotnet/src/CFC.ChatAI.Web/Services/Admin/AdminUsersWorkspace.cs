using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Api;
using CFC.ChatAI.Web.Services.Auth;

namespace CFC.ChatAI.Web.Services.Admin;

public enum AdminUserAction { ChangeRole, Deactivate, Reactivate, Delete }
public sealed record AdminUserChange(AdminUser Target, AdminUserAction Action, string? NewRole);

public sealed class AdminUsersWorkspace(ChatApiClient api, SupabaseSessionService session, string ownerId) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    public static IReadOnlyList<string> AssignableRoles { get; } = Array.AsReadOnly(new[] { "user", "dev", "admin" });
    public IReadOnlyList<AdminUser> Users { get; private set; } = [];
    public AdminUserChange? Pending { get; private set; }
    public string? Notice { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public bool Loaded { get; private set; }

    private bool Authorized => !lifetime.IsCancellationRequested && session.HasAdminAccess(ownerId);

    public bool CanManage(AdminUser user) => Authorized && user.Id != ownerId && user.Role != "superuser";

    public bool CanTakeAction(AdminUser user, AdminUserAction action) => CanManage(user) && (action switch
    {
        AdminUserAction.ChangeRole => true,
        AdminUserAction.Deactivate => session.AccountLifecycleEnabled && user.Status == "active",
        AdminUserAction.Reactivate => session.AccountLifecycleEnabled && user.Status == "inactive",
        AdminUserAction.Delete => session.PermanentAccountDeletionEnabled && user.Status is ("active" or "inactive"),
        _ => false
    });
    public Task LoadAsync() => RunAsync(async () =>
    {
        Pending = null;
        await ReadUsersAsync();
    });

    private async Task ReadUsersAsync()
    {
        Loaded = false;
        Users = [];
        var result = await RequestAsync(api.GetAdminUsersAsync);
        Users = result.Users;
        Loaded = true;
    }

    public Task InviteAsync(string email) => RunAsync(async () =>
    {
        email = email.Trim();
        if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
        {
            Error = "Enter a valid email address.";
            return;
        }
        var invitation = await RequestAsync((token, ct) => api.InviteUserAsync(token, new(email), ct));
        Notice = invitation.Message;
    });

    public void BeginChange(AdminUser target, AdminUserAction action, string? newRole = null)
    {
        if (Busy || Pending is not null || !Loaded || !Users.Contains(target) || !CanTakeAction(target, action)) return;
        if (action == AdminUserAction.ChangeRole && (!AssignableRoles.Contains(newRole) || newRole == target.Role)) return;
        Notice = Error = null;
        Pending = new(target, action, newRole);
    }

    public void CancelChange() { if (!Busy) Pending = null; }

    public Task ConfirmAsync(AdminUserChange change)
    {
        if (Busy || !ReferenceEquals(Pending, change)) return Task.CompletedTask;
        Pending = null;
        return RunAsync(async () =>
        {
            // Another administrator may have changed this account while confirmation was open.
            await ReadUsersAsync();
            var target = Users.FirstOrDefault(row => row.Id == change.Target.Id);
            if (target is null || !CanTakeAction(target, change.Action) || target.Status != change.Target.Status ||
                target.Role != change.Target.Role || target.Email != change.Target.Email)
            {
                Error = "This account changed. Review the refreshed list before taking action.";
                return;
            }
            // An uncertain write requires a fresh list before another action.
            Loaded = false;
            var confirmed = await RequestAsync(async (token, ct) =>
            {
                switch (change.Action)
                {
                    case AdminUserAction.ChangeRole:
                        var role = await api.ChangeUserRoleAsync(token, new(target.Id, change.NewRole!), ct);
                        return role.UserId == target.Id && role.NewRole == change.NewRole ? role.Message : null;
                    case AdminUserAction.Deactivate:
                        var inactive = await api.DeactivateUserAsync(token, target.Id, ct);
                        return inactive.UserId == target.Id && inactive.Status == "inactive" ? inactive.Message : null;
                    case AdminUserAction.Reactivate:
                        var active = await api.ReactivateUserAsync(token, target.Id, ct);
                        return active.UserId == target.Id && active.Status == "active" ? active.Message : null;
                    case AdminUserAction.Delete:
                        var deleted = await api.DeleteUserAsync(token, target.Id, ct);
                        return deleted.UserId == target.Id && deleted.PermanentlyDeleted ? deleted.Message : null;
                    default: throw new InvalidOperationException();
                }
            });
            if (confirmed is null) Error = "The server did not confirm the requested change. Review the refreshed list.";
            else Notice = confirmed;
            await ReadUsersAsync();
        });
    }

    private Task<T> RequestAsync<T>(Func<string, CancellationToken, Task<T>> request)
    {
        if (lifetime.IsCancellationRequested) throw new OperationCanceledException();
        return session.WithAdminTokenAsync(ownerId, request, lifetime.Token);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Busy || lifetime.IsCancellationRequested) return;
        Busy = true;
        Error = Notice = null;
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            Error = ex switch
            {
                HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                    "Administrator access could not be verified. Refresh or sign in again.",
                ChatApiException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity } rejection =>
                    rejection.Message,
                _ when Notice is not null => "The API confirmed the change, but the user list could not be refreshed.",
                _ => "Could not confirm the request with the API. Refresh the user list before retrying."
            };
            if (!Authorized || ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            {
                Pending = null;
                Users = [];
                Loaded = false;
                Notice = null;
            }
        }
        finally { Busy = false; }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

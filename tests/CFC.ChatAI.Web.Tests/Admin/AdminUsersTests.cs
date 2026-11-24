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
    private static readonly AdminUser Target = new("member", "member@example.test", "user", "active", "2026-01-01", "Member");

    private static HttpResponseMessage AdminList(params AdminUser[] users) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new AdminUserList(users, users.Length)) };

    private async Task RestoreRole(string role)
    {
        browser.Session = Token();
        backend.Profile = _ => Task.FromResult(ActiveProfile() with { Role = role });
        await session.RestoreAsync();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task AdminConsoleRendersUsersAndRequiresExplicitDeleteConfirmation(string role)
    {
        await RestoreRole(role);
        var writes = 0;
        using var http = HistoryHttp(request =>
        {
            Assert.Equal(browser.Session, request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Get) return writes == 0 ? AdminList(Target) : AdminList();
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/admin/users/member", request.RequestUri!.AbsolutePath);
            writes++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DeleteUserResponse("Deleted member", Target.Id, Target.Email, true)) };
        });
        using var services = HistoryServices(http, new TestNavigation("admin"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            await page.QuiescenceTask;
            Assert.Contains("id=\"upload-tab\"", page.ToHtmlString());
            Assert.Contains(Target.Email, page.ToHtmlString());
            Assert.Contains("Status: active", page.ToHtmlString());
            await renderer.ClickButtonAsync<AdminUsers>("Delete");
            Assert.Contains("Permanently delete member@example.test", page.ToHtmlString());
            await renderer.ClickButtonAsync<AdminUsers>("Cancel");
            Assert.DoesNotContain("Confirm deletion", page.ToHtmlString());
            Assert.Equal(0, writes);
            await renderer.ClickButtonAsync<AdminUsers>("Delete");
            await renderer.ClickButtonAsync<AdminUsers>("Confirm deletion");
            Assert.Equal(1, writes);
            Assert.Contains("Deleted member", page.ToHtmlString());
            Assert.Contains("No registered users", page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(AdminUserAction.ChangeRole)]
    [InlineData(AdminUserAction.Deactivate)]
    [InlineData(AdminUserAction.Reactivate)]
    public async Task ConfirmedChangeReloadsServerStateAndCannotBeReplayed(AdminUserAction action)
    {
        await RestoreRole("admin");
        var row = action == AdminUserAction.Reactivate ? Target with { Status = "inactive" } : Target;
        var writes = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.Method == HttpMethod.Get) return AdminList(row);
            Assert.Equal(HttpMethod.Post, req.Method);
            writes++;
            row = row with { Status = action == AdminUserAction.Deactivate ? "inactive" : "active",
                Role = action == AdminUserAction.ChangeRole ? "dev" : "user" };
            object response = action switch
            {
                AdminUserAction.ChangeRole => new ChangeRoleResponse("Changed", row.Id, row.Role),
                AdminUserAction.Deactivate => new DeactivateUserResponse("Changed", row.Id, row.Email, row.Status, "now"),
                _ => new ReactivateUserResponse("Changed", row.Id, row.Email, row.Status, "now")
            };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.BeginChange(Assert.Single(work.Users), action, action == AdminUserAction.ChangeRole ? "dev" : null);
        var change = Assert.IsType<AdminUserChange>(work.Pending);
        Assert.Equal(0, writes);
        await work.ConfirmAsync(change);
        await work.ConfirmAsync(change);
        Assert.Equal(1, writes);
        Assert.Equal(row, Assert.Single(work.Users));
        Assert.Equal("Changed", work.Notice);
    }

    [Theory]
    [InlineData("user-a", "admin")]
    [InlineData("member", "superuser")]
    public async Task OwnAccountAndSuperusersHaveNoManagementActions(string id, string role)
    {
        await RestoreRole("superuser");
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return AdminList(Target with { Id = id, Role = role });
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        var target = Assert.Single(work.Users);
        Assert.False(work.CanManage(target));
        foreach (var action in Enum.GetValues<AdminUserAction>())
        {
            work.BeginChange(target, action, "user");
            Assert.Null(work.Pending);
        }
    }

    [Theory]
    [InlineData("false", "true", false, false)]
    [InlineData("true", "false", true, false)]
    [InlineData("true", "true", true, true)]
    public async Task AccountActionsHonorBothDeploymentFlags(string lifecycle, string permanent, bool canDeactivate, bool canDelete)
    {
        backend.Config = backend.Config with { AccountLifecycle = lifecycle, PermanentAccountDeletion = permanent };
        await RestoreRole("admin");
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return AdminList(Target);
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        Assert.Equal(canDeactivate, work.CanTakeAction(Target, AdminUserAction.Deactivate));
        Assert.Equal(canDelete, work.CanTakeAction(Target, AdminUserAction.Delete));
        work.BeginChange(Target, AdminUserAction.Delete);
        Assert.Equal(canDelete, work.Pending is not null);
    }

    [Fact]
    public async Task RejectsUnsupportedRoleAndStaleConfirmationForNewlyProtectedAccount()
    {
        await RestoreRole("admin");
        var reads = 0;
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return AdminList(reads++ == 0 ? Target : Target with { Role = "superuser" });
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.BeginChange(Target, AdminUserAction.ChangeRole, "superuser");
        Assert.Null(work.Pending);
        work.BeginChange(Target, AdminUserAction.Delete);
        await work.ConfirmAsync(work.Pending!);
        Assert.Contains("account changed", work.Error);
        Assert.Equal("superuser", Assert.Single(work.Users).Role);
    }

    [Theory]
    [InlineData("user", "user-a")]
    [InlineData("dev", "user-a")]
    [InlineData("admin", "different-owner")]
    public async Task UnauthorizedWorkspaceNeverReachesTransport(string role, string owner)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unauthorized dispatch."));
        using var work = new AdminUsersWorkspace(new(http), session, owner);
        await work.LoadAsync();
        Assert.False(work.Loaded);
        await work.InviteAsync("invitee@example.test");
        Assert.Empty(work.Users);
        Assert.NotNull(work.Error);
    }

    [Fact]
    public async Task InvitationHandlesInvalidEmailConflictAndSuccessWithoutAddingARegisteredUser()
    {
        await RestoreRole("admin");
        var attempts = 0;
        using var http = HistoryHttp(req =>
        {
            Assert.Equal("/api/admin/invite", req.RequestUri!.AbsolutePath);
            Assert.Equal(browser.Session, req.Headers.Authorization?.Parameter);
            return ++attempts == 1
                ? new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { detail = "Pending invitation" }) }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(new AdminInvitation("Invitation created", "i@example.test", "later")) };
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.InviteAsync("invalid");
        Assert.Equal(0, attempts);
        await work.InviteAsync("i@example.test");
        Assert.Equal("Pending invitation", work.Error);
        await work.InviteAsync(" i@example.test ");
        Assert.Null(work.Error);
        Assert.Equal("Invitation created", work.Notice);
        Assert.Empty(work.Users);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task UnconfirmedMutationDoesNotDisplaySuccessAndRequiresFreshList(int code)
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req => req.Method == HttpMethod.Get ? AdminList(Target) :
            new((HttpStatusCode)code) { Content = JsonContent.Create(new { detail = "internal error detail" }) });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.BeginChange(Target, AdminUserAction.Delete);
        await work.ConfirmAsync(work.Pending!);
        Assert.False(work.Loaded);
        Assert.Null(work.Notice);
        Assert.NotNull(work.Error);
        Assert.DoesNotContain("internal error detail", work.Error);
        work.BeginChange(Target, AdminUserAction.Delete);
        Assert.Null(work.Pending);
        await work.LoadAsync();
        Assert.Equal(Target, Assert.Single(work.Users));
    }

    [Fact]
    public async Task DowngradeDuringConfirmationDiscardsListAndPreventsWrite()
    {
        await RestoreRole("admin");
        var pendingRead = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var http = new HttpClient(new HistoryHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return ++reads == 1 ? Task.FromResult(AdminList(Target)) : pendingRead.Task;
        })) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.BeginChange(Target, AdminUserAction.Delete);
        var change = work.Pending!;
        var confirmation = work.ConfirmAsync(change);
        await work.ConfirmAsync(change);
        backend.Profile = _ => Task.FromResult(ActiveProfile());
        await session.RestoreAsync();
        pendingRead.SetResult(AdminList(Target));
        await confirmation;
        Assert.Equal(2, reads);
        Assert.Empty(work.Users);
        Assert.Null(work.Notice);
        Assert.False(work.Loaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteDistinguishesUnconfirmedResponseFromConfirmedWriteAndFailedReload(bool deleted)
    {
        await RestoreRole("admin");
        var writes = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.Method == HttpMethod.Get)
                return writes == 0 || !deleted ? AdminList(Target) : new(HttpStatusCode.ServiceUnavailable);
            writes++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DeleteUserResponse("Deleted", Target.Id, Target.Email, deleted)) };
        });
        using var work = new AdminUsersWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.BeginChange(Target, AdminUserAction.Delete);
        await work.ConfirmAsync(work.Pending!);
        Assert.Equal(1, writes);
        Assert.Equal(deleted ? "Deleted" : null, work.Notice);
        Assert.Contains(deleted ? "could not be refreshed" : "did not confirm", work.Error);
    }
}

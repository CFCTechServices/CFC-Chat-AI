using System.Text.Json.Serialization;
using CFC.ChatAI.Api.Security;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Invitations;
using CFC.ChatAI.Supabase.Profiles;
using Microsoft.AspNetCore.Mvc;

namespace CFC.ChatAI.Api.Endpoints;

internal static class AccountLifecycleEndpoints
{
    public static void MapAccountLifecycle(this WebApplication app)
    {
        var users = app.MapGroup("/api/admin/users/{userId}").RequireAuthorization("admin");
        users.MapPost("/deactivate", (string userId, DeactivateRequest? request, HttpContext context,
            AccountLifecycleWorkflow workflow) => request?.Reason is { } reason && (reason.Length > 500 || reason.Any(char.IsControl))
                ? Task.FromResult(Error(400, "Invalid deactivation reason"))
                : workflow.RunAsync(AccountAction.Deactivate, userId, context))
            .WithMetadata(new RequestSizeLimitAttribute(4096));
        users.MapPost("/reactivate", (string userId, HttpContext context, AccountLifecycleWorkflow workflow) =>
            workflow.RunAsync(AccountAction.Reactivate, userId, context));
        users.MapDelete("", (string userId, HttpContext context, AccountLifecycleWorkflow workflow) =>
            workflow.RunAsync(AccountAction.Delete, userId, context));
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DeactivateRequest(string? Reason);
    internal static IResult Error(int status, string detail) => Results.Json(new { detail }, statusCode: status);
}

internal sealed class AccountLifecycleWorkflow(IAdminProfileRepository profiles, IAdminIdentityGateway auth,
    IInvitationRepository invitations, TimeProvider clock, AccountLifecycleJournal journal, ILogger<AccountLifecycleWorkflow> logger)
{
    public async Task<IResult> RunAsync(AccountAction action, string userId, HttpContext context)
    {
        if (!Guid.TryParse(userId, out var target) || target == Guid.Empty)
            return AccountLifecycleEndpoints.Error(400, "Invalid user ID");
        var actor = Guid.Parse(context.User.Id());
        if (target == actor) return AccountLifecycleEndpoints.Error(400, "Cannot modify your own account");
        if (!journal.IsEnabled(action)) return AccountLifecycleEndpoints.Error(503, "Account operation is not enabled");
        var id = target.ToString();
        using var lease = journal.Slots.TryEnter(id);
        if (lease is null) return AccountLifecycleEndpoints.Error(409, "Another account operation is in progress; retry later");
        var ct = context.RequestAborted;
        AccountOperation? operation = null;
        var step = "journal";
        try
        {
            ct.ThrowIfCancellationRequested();
            var root = journal.RequireRoot();
            operation = AccountLifecycleJournal.Read(root, id);
            if (operation is not null && operation.Action != action)
                return Incomplete(operation, 409, "A different account operation is pending; retry that operation or contact an operator.");

            step = "read_profile";
            var profile = await profiles.GetByIdAsync(id, ct);
            if (profile?.Role == "superuser") return AccountLifecycleEndpoints.Error(400, "Cannot modify a superuser account");
            if (operation is null)
            {
                if (profile is null) return AccountLifecycleEndpoints.Error(404, "User not found");
                if (!CanStart(action, profile)) return AccountLifecycleEndpoints.Error(409, "Account is not eligible for this operation");
            }
            else if (!operation.Matches(profile) && !operation.IsResult(profile) &&
                !(profile is null && operation is { Action: AccountAction.Delete, DeleteRequested: true }))
                return Incomplete(operation, 409, "Profile changed during an account operation; contact an operator.");

            step = "read_auth";
            var identity = await auth.GetUserAsync(id, ct);
            if (identity is null && profile is null && operation is { Action: AccountAction.Delete, DeleteRequested: true })
                return Complete(root, operation);
            var email = identity?.Email;
            if (profile is null || identity?.Id != id || string.IsNullOrWhiteSpace(email) ||
                !string.Equals(email, profile.Email, StringComparison.OrdinalIgnoreCase) ||
                (operation is not null && !string.Equals(email, operation.Email, StringComparison.Ordinal)))
                return operation is null ? AccountLifecycleEndpoints.Error(409, "Auth identity is missing or does not match the profile") :
                    Incomplete(operation, 409, "Auth identity is missing or does not match the pending operation; contact an operator.");

            if (operation is null)
            {
                step = "start_journal";
                var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
                var record = new AccountOperation(Guid.NewGuid(), id, actor.ToString(), action,
                    startedAt, email, profile.Role, profile.Status, profile.DeletedAt, profile.DeletedBy);
                ct.ThrowIfCancellationRequested();
                AccountLifecycleJournal.Save(root, record, overwrite: false);
                operation = record;
            }
            if (operation.IsResult(profile)) return Complete(root, operation);

            if (action == AccountAction.Delete)
            {
                if (!operation.DeleteRequested)
                {
                    step = "delete_invitations";
                    await invitations.DeleteByEmailAsync(email, ct);
                    step = "record_delete_intent";
                    operation = operation with { DeleteRequested = true };
                    AccountLifecycleJournal.Save(root, operation, overwrite: true);
                }
                step = "confirm_target";
                if (!operation.Matches(await profiles.GetByIdAsync(id, ct)))
                    return Incomplete(operation, 409, "Profile changed during deletion; contact an operator.");
                step = "delete_auth";
                await auth.DeleteUserAsync(id, ct);
                step = "verify_deletion";
                if (await auth.GetUserAsync(id, ct) is not null || await profiles.GetByIdAsync(id, ct) is not null)
                    return Incomplete(operation, 500, "Deletion is not confirmed; contact an operator.");
            }
            else
            {
                var deactivate = action == AccountAction.Deactivate;
                step = deactivate ? "ban_auth" : "unban_auth";
                await auth.SetBannedAsync(id, deactivate, ct);
                if (deactivate)
                {
                    step = "revoke_sessions";
                    await auth.RevokeSessionsAsync(id, ct);
                }
                step = "update_profile";
                var changed = await profiles.TryUpdateAccountStateAsync(profile, deactivate ? "inactive" : "active",
                    deactivate ? operation.StartedAt : null, deactivate ? operation.ActorId : null, ct);
                if (!operation.IsResult(changed))
                    return Incomplete(operation, 409, "Auth changed but the profile update is not confirmed; retry the same action or contact an operator.");
            }
            return Complete(root, operation);

            IResult Complete(string path, AccountOperation record)
            {
                step = "complete_journal";
                AccountLifecycleJournal.Complete(path, record);
                return Success(record);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Account lifecycle action {Action} stopped at {Step} (operation {Operation}, trace {TraceId})",
                action, step, operation?.Reference, context.TraceIdentifier);
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            var status = ex is DependencyFailureException || LocalDirectory.IsUnavailable(ex) ? 503 : 500;
            return operation is null ? AccountLifecycleEndpoints.Error(status, "Account operation could not start") :
                Incomplete(operation, status, "Account operation may be incomplete; retry the same action or contact an operator.");
        }
    }

    private static bool CanStart(AccountAction action, Profile profile) => profile.Role is ("user" or "dev" or "admin") && action switch
    {
        AccountAction.Deactivate => profile is { Status: "active", DeletedAt: null, DeletedBy: null },
        AccountAction.Reactivate => profile.Status == "inactive",
        AccountAction.Delete => profile.Status == "inactive" || profile is { Status: "active", DeletedAt: null, DeletedBy: null },
        _ => false,
    };

    private IResult Success(AccountOperation record) => record.Action switch
    {
        AccountAction.Deactivate => Results.Ok(new { message = "User deactivated.", record.UserId, record.Email, status = "inactive", deleted_at = record.StartedAt }),
        AccountAction.Reactivate => Results.Ok(new { message = "User reactivated.", record.UserId, record.Email, status = "active", restored_at = clock.GetUtcNow() }),
        _ => Results.Ok(new { message = "User permanently deleted.", record.UserId, record.Email, permanently_deleted = true }),
    };

    private static IResult Incomplete(AccountOperation operation, int status, string detail) =>
        Results.Json(new { detail, reference = operation.Reference }, statusCode: status);
}

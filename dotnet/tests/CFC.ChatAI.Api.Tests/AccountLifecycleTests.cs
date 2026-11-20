using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Invitations;
using CFC.ChatAI.Supabase.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static CFC.ChatAI.Api.Tests.DocumentUploadTests;

namespace CFC.ChatAI.Api.Tests;

public sealed class AccountLifecycleTests : IDisposable
{
    private const string AdminId = "cf52745a-430b-4fef-91d1-9287cbbf2c33";
    private const string TargetId = "db14cc3c-7005-419b-ba68-9d1e8053a31f";
    private const string Target = "/api/admin/users/" + TargetId;
    private readonly string _journal = Directory.CreateTempSubdirectory().FullName;
    private readonly Providers _providers = new();

    private WebApplicationFactory<Program> Setup(string role = "admin", string status = "active", bool deleted = false,
        bool enabled = true, bool hardDelete = true, bool journal = true) => Factory(role: role, status: status, deleted: deleted)
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:EnableAccountLifecycle", enabled.ToString());
            builder.UseSetting("Api:EnablePermanentAccountDeletion", hardDelete.ToString());
            builder.UseSetting("Api:AccountLifecycleJournalDirectory", journal ? _journal : "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAdminIdentityGateway>(); services.AddSingleton<IAdminIdentityGateway>(_providers);
                services.RemoveAll<IAdminProfileRepository>(); services.AddSingleton<IAdminProfileRepository>(_providers);
                services.RemoveAll<IInvitationRepository>(); services.AddSingleton<IInvitationRepository>(_providers);
            });
        });

    [Fact]
    public async Task Protects_every_route_and_does_not_expose_profile_writes()
    {
        using var factory = Setup(); using var anonymous = Client(factory, null);
        using var user = Client(Setup(role: "user")); using var inactive = Client(Setup(status: "inactive"));
        using var deleted = Client(Setup(deleted: true)); using var admin = Client(factory);
        foreach (var client in new[] { anonymous, user, inactive, deleted })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.PostAsync(Target + "/deactivate", null)).StatusCode);
            Assert.Equal(expected, (await client.PostAsync(Target + "/reactivate", null)).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync(Target)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.PatchAsJsonAsync("/api/profile/me", new { full_name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/profile/complete", new { full_name = "Test" })).StatusCode);
        Assert.Empty(_providers.Calls);
    }

    [Fact]
    public async Task Flags_journal_validation_and_target_guards_prevent_mutation()
    {
        using var off = Client(Setup(enabled: false)); using var noDelete = Client(Setup(hardDelete: false));
        using var missingJournal = Client(Setup(journal: false)); using var admin = Client(Setup());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await off.PostAsync(Target + "/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await noDelete.DeleteAsync(Target)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await missingJournal.PostAsync(Target + "/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/admin/users/garbage")).StatusCode);
        var self = "/api/admin/users/" + AdminId.ToUpperInvariant();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync(self)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(self + "/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Target + "/deactivate", new { reason = new string('x', 501) })).StatusCode);
        Assert.Empty(_providers.Calls);
        _providers.Profile = _providers.Profile! with { Role = "superuser" };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync(Target)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(Target + "/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(Target + "/deactivate", null)).StatusCode);
        _providers.Profile = _providers.Profile with { Role = "user", Status = "deleted", DeletedAt = DateTimeOffset.UtcNow };
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(Target + "/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Target)).StatusCode);
        _providers.Profile = _providers.Profile with { Status = "active", DeletedAt = null };
        _providers.Identity = null;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(Target + "/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Target)).StatusCode);
        _providers.Profile = null;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync(Target)).StatusCode);
        Assert.Empty(Directory.GetFiles(_journal));
        Assert.All(_providers.Calls, name => Assert.Contains(name, new[] { "profile", "identity" }));
    }

    [Theory]
    [InlineData("ban", false)]
    [InlineData("revoke", false)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task Deactivation_retries_after_each_provider_failure_and_survives_host_restart(string stage, bool afterWrite)
    {
        using var factory = Setup(); using var client = Client(factory);
        _providers.Fault = stage; _providers.AfterWrite = afterWrite;
        using var failure = await client.PostAsJsonAsync(Target + "/deactivate", new { reason = "private reason" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        var error = await failure.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, error.GetProperty("reference").ValueKind);
        Assert.False(error.ToString().Contains("private", StringComparison.Ordinal));
        var pending = Assert.Single(Directory.GetFiles(_journal, "*.pending.json"));
        Assert.DoesNotContain("private reason", File.ReadAllText(pending));
        Assert.Equal(afterWrite ? "inactive" : "active", _providers.Profile!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(Target + "/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/change-role", new { user_id = TargetId, new_role = "admin" })).StatusCode);
        _providers.Fault = null;
        using var restart = Setup(); using var retry = Client(restart);
        using var success = await retry.PostAsync(Target + "/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var body = await success.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TargetId, body.GetProperty("user_id").GetString());
        Assert.Equal("inactive", body.GetProperty("status").GetString());
        Assert.Equal("target@example.test", body.GetProperty("email").GetString());
        Assert.Equal(body.GetProperty("deleted_at").GetDateTimeOffset(), _providers.Profile!.DeletedAt);
        Assert.Equal(AdminId, _providers.Profile.DeletedBy);
        Assert.True(_providers.Banned);
        Assert.Empty(Directory.GetFiles(_journal, "*.pending.json"));
        Assert.Single(Directory.GetFiles(_journal, "*.audit.json"));
        Assert.Equal(HttpStatusCode.Conflict, (await retry.PostAsync(Target + "/deactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData("unban", false)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task Reactivation_requires_inactive_status_not_absence_of_deactivation_metadata(string stage, bool afterWrite)
    {
        _providers.Profile = _providers.Profile! with { Status = "inactive", DeletedAt = DateTimeOffset.UtcNow, DeletedBy = AdminId };
        _providers.Fault = stage; _providers.AfterWrite = afterWrite; _providers.Banned = true;
        using var factory = Setup(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync(Target + "/reactivate", null)).StatusCode);
        _providers.Fault = null;
        using var restored = await client.PostAsync(Target + "/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var body = await restored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal(TargetId, body.GetProperty("user_id").GetString());
        Assert.True(body.TryGetProperty("restored_at", out _));
        Assert.Null(_providers.Profile!.DeletedAt);
        Assert.Null(_providers.Profile.DeletedBy);
        Assert.False(_providers.Banned);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(Target + "/reactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData("invitations", false)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Hard_delete_requires_confirmed_auth_and_profile_absence_and_retries_after_partial_completion(string stage, bool afterWrite)
    {
        _providers.Fault = stage; _providers.AfterWrite = afterWrite;
        using var factory = Setup(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.DeleteAsync(Target)).StatusCode);
        Assert.Equal(stage != "invitations", _providers.Calls.Contains("delete"));
        var failedInvitesCalls = stage == "invitations" ? 2 : 1;
        _providers.Fault = null;
        using var success = await client.DeleteAsync(Target);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var result = await success.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("permanently_deleted").GetBoolean());
        Assert.Equal(TargetId, result.GetProperty("user_id").GetString());
        Assert.Equal("target@example.test", result.GetProperty("email").GetString());
        Assert.Null(_providers.Identity);
        Assert.Null(_providers.Profile);
        Assert.Equal(failedInvitesCalls, _providers.Calls.Count(x => x == "invitations"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Target)).StatusCode);
    }

    [Fact]
    public async Task Hard_delete_with_no_profile_cascade_does_not_claim_success()
    {
        _providers.Cascade = false;
        using var factory = Setup(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.DeleteAsync(Target)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Target)).StatusCode);
        Assert.Single(Directory.GetFiles(_journal, "*.pending.json"));
    }

    [Fact]
    public async Task Conditional_conflict_does_not_overwrite_state_and_cancellation_preserves_pending_record()
    {
        using var factory = Setup(); using var client = Client(factory);
        _providers.PauseBan = true;
        using var cancel = new CancellationTokenSource();
        var request = client.PostAsync(Target + "/deactivate", null, cancel.Token);
        await _providers.BanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(Target)).StatusCode);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await _providers.BanCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(Directory.GetFiles(_journal, "*.pending.json"));
        _providers.PauseBan = false;
        _providers.ConflictOnWrite = true;
        using var restart = Setup(); using var retry = Client(restart);
        using var result = await retry.PostAsync(Target + "/deactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("profile update is not confirmed", await result.Content.ReadAsStringAsync());
        Assert.Equal("active", _providers.Profile!.Status);
    }

    public void Dispose() => Directory.Delete(_journal, recursive: true);

    private sealed class Providers : IAdminProfileRepository, IAdminIdentityGateway, IInvitationRepository
    {
        public List<string> Calls { get; } = [];
        public Profile? Profile { get; set; } = new(TargetId, "target@example.test", null, null, "user", "active", DateTimeOffset.UtcNow, null, null);
        public IdentityUser? Identity { get; set; } = new(TargetId, "target@example.test");
        public string? Fault { get; set; }
        public bool AfterWrite { get; set; }
        public bool Banned { get; set; }
        public bool Cascade { get; set; } = true;
        public bool PauseBan { get; set; }
        public bool ConflictOnWrite { get; set; }
        public TaskCompletionSource BanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BanCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void Visit(string stage, Action? write = null)
        {
            Calls.Add(stage);
            if (Fault == stage && !AfterWrite) throw new DependencyFailureException("private provider", stage);
            write?.Invoke();
            if (Fault == stage) throw new DependencyFailureException("private provider", stage);
        }
        public Task<Profile?> GetByIdAsync(string id, CancellationToken ct = default) { Visit("profile"); return Task.FromResult(id == TargetId ? Profile : null); }
        public Task<IdentityUser?> GetUserAsync(string id, CancellationToken ct = default) { Visit("identity"); return Task.FromResult(id == TargetId ? Identity : null); }
        public async Task SetBannedAsync(string id, bool banned, CancellationToken ct = default)
        {
            Visit(banned ? "ban" : "unban", () => Banned = banned);
            if (!PauseBan) return;
            BanStarted.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { BanCanceled.TrySetResult(); throw; }
        }
        public Task RevokeSessionsAsync(string id, CancellationToken ct = default) { Visit("revoke"); return Task.CompletedTask; }
        public Task DeleteUserAsync(string id, CancellationToken ct = default)
        {
            Visit("delete", () => { Identity = null; if (Cascade) Profile = null; });
            return Task.CompletedTask;
        }
        public Task DeleteByEmailAsync(string email, CancellationToken ct = default) { Assert.Equal(Identity!.Email, email); Visit("invitations"); return Task.CompletedTask; }
        public Task<Profile?> TryUpdateAccountStateAsync(Profile expected, string status, DateTimeOffset? at, string? by, CancellationToken ct = default)
        {
            if (ConflictOnWrite || Profile != expected) return Task.FromResult<Profile?>(null);
            Visit("update", () => Profile = expected with { Status = status, DeletedAt = at, DeletedBy = by });
            return Task.FromResult<Profile?>(Profile);
        }
        public Task<Profile?> UpdateAccountStateAsync(string id, string status, DateTimeOffset? at, string? by, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Profile?> UpdateRoleAsync(string id, string role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Profile?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Profile>> ListAsync(PageRequest page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Invitation?> GetLatestByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Invitation?> GetUnregisteredByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Invitation> InsertAsync(CreateInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

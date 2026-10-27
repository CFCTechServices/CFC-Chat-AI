using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.Supabase.Conversations;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Invitations;
using CFC.ChatAI.Supabase.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.Api.Tests;

public sealed class InternalApiContractTests
{
    private const string UserId = "cf52745a-430b-4fef-91d1-9287cbbf2c33";
    private const string SessionId = "c175cddd-8b1b-4891-bd9f-1ecf4c47b081";

    private static WebApplicationFactory<Program> Factory(string role = "user", bool active = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SUPABASE_URL", "https://example.supabase.co");
            builder.UseSetting("SUPABASE_ANON_KEY", "public-anon");
            builder.UseSetting("SUPABASE_SERVICE_ROLE_KEY", "private-service");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdentityGateway>();
                services.RemoveAll<IUserProfileRepository>();
                services.RemoveAll<IConversationRepository>();
                services.AddSingleton<IIdentityGateway>(new Identity());
                services.AddSingleton<IUserProfileRepository>(new Profiles(role, active));
                services.AddSingleton<IConversationRepository>(new Conversations());
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? token = "valid")
    {
        var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Config_ExposesAnonKeyOnly_WithoutAuthentication()
    {
        using var factory = Factory();
        using var client = Client(factory, null);
        var json = await client.GetFromJsonAsync<JsonElement>("/api/auth/config");
        Assert.Equal("public-anon", json.GetProperty("supabaseKey").GetString());
        Assert.Equal("https://example.supabase.co", json.GetProperty("supabaseUrl").GetString());
        Assert.DoesNotContain("private-service", json.ToString());
    }

    [Fact]
    public async Task InvalidJson_MapsToSafe400()
    {
        using var factory = Factory();
        using var client = Client(factory, null);
        using var body = new StringContent("{invalid", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/auth/check-email", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Supabase", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UserRoutes_RequireTokenAndActiveProfile()
    {
        using var factory = Factory();
        using var client = Client(factory, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile/me")).StatusCode);
        using var invalid = Client(factory, "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.GetAsync("/api/profile/me")).StatusCode);
        using var inactiveFactory = Factory(active: false);
        using var inactive = Client(inactiveFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await inactive.GetAsync("/api/profile/me")).StatusCode);
    }

    [Fact]
    public async Task AdminRoutes_RejectOrdinaryUsers_AndProfileHasSnakeCase()
    {
        using var factory = Factory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/profile/me");
        Assert.Equal(UserId, profile.GetProperty("id").GetString());
        Assert.Equal("User", profile.GetProperty("full_name").GetString());
        Assert.Equal("active", profile.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DetailedSessions_MapPreviewAndUseBoundedRepositoryPage()
    {
        using var factory = Factory();
        using var client = Client(factory);
        var body = await client.GetFromJsonAsync<JsonElement>("/api/chat/sessions?detail=true");
        var session = body.EnumerateArray().Single();
        Assert.Equal(2, session.GetProperty("message_count").GetInt32());
        Assert.Equal("hello", session.GetProperty("last_message").GetString());
        Assert.Equal(100, (factory.Services.GetRequiredService<IConversationRepository>() as Conversations)!.LastPageSize);
    }

    [Fact]
    public async Task AdminSettings_RejectsUnknownFieldsAndPersistsAtomicPatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cfc-api-settings-{Guid.NewGuid():N}.json");
        try
        {
            using var factory = Factory("admin").WithWebHostBuilder(builder => builder.UseSetting("Api:AdminSettingsPath", path));
            using var client = Client(factory);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/admin/settings", new { unexpected = true })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/admin/settings", new { })).StatusCode);
            var changed = await client.PatchAsJsonAsync("/api/admin/settings", new { maintenance_mode = true });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var json = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings");
            Assert.True(json.GetProperty("maintenance_mode").GetBoolean());
            Assert.True(json.GetProperty("email_notifications").GetBoolean());
            await File.WriteAllTextAsync(path, "not json");
            Assert.Equal(HttpStatusCode.InternalServerError,
                (await client.PatchAsJsonAsync("/api/admin/settings", new { maintenance_mode = false })).StatusCode);
            Assert.Equal("not json", await File.ReadAllTextAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AdminInvitation_PreservesWhitelistShape_AndDoesNotPromiseEmailDelivery()
    {
        using var factory = Factory("admin").WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IInvitationRepository>();
            services.RemoveAll<IAdminProfileRepository>();
            services.AddSingleton<IInvitationRepository>(new Invitations());
            services.AddSingleton<IAdminProfileRepository>(new AdminProfiles());
        }));
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync("/api/admin/invite", new { email = "new@example.com" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("new@example.com", json.GetProperty("email").GetString());
        Assert.Contains("Invitation created", json.GetProperty("message").GetString());
        Assert.Equal(UserId, factory.Services.GetRequiredService<IInvitationRepository>() is Invitations invites ? invites.CreatedBy : null);
    }

    [Fact]
    public async Task SessionHistory_UsesCallerToken_AndReturns404ForUnownedSession()
    {
        using var factory = Factory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chat/sessions/{SessionId}")).StatusCode);
        var sessions = factory.Services.GetRequiredService<IConversationRepository>() as Conversations;
        Assert.Equal(UserId, sessions!.LastUser);
        Assert.Equal("valid", sessions.LastToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/chat/sessions/not-a-guid")).StatusCode);
    }

    private sealed class Invitations : IInvitationRepository
    {
        public string? CreatedBy { get; private set; }
        public Task<Invitation?> GetLatestByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<Invitation?>(null);
        public Task<Invitation?> GetUnregisteredByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<Invitation?>(null);
        public Task<Invitation> InsertAsync(CreateInvitation value, CancellationToken ct = default)
        {
            CreatedBy = value.CreatedBy;
            return Task.FromResult(new Invitation(Guid.NewGuid().ToString(), value.Email, value.CreatedBy, null, false,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        }
        public Task DeleteByIdAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteByEmailAsync(string email, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AdminProfiles : IAdminProfileRepository
    {
        public Task<Page<Profile>> ListAsync(PageRequest page, CancellationToken ct = default) => Task.FromResult(new Page<Profile>([], false));
        public Task<Profile?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Profile?>(null);
        public Task<Profile?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<Profile?>(null);
        public Task<Profile?> UpdateRoleAsync(string id, string role, CancellationToken ct = default) => Task.FromResult<Profile?>(null);
        public Task<Profile?> UpdateAccountStateAsync(string id, string status, DateTimeOffset? at, string? by, CancellationToken ct = default) => Task.FromResult<Profile?>(null);
    }

    private sealed class Identity : IIdentityGateway
    {
        public Task<IdentityUser?> GetUserAsync(string token, CancellationToken ct = default) =>
            Task.FromResult<IdentityUser?>(token == "valid" ? new IdentityUser(UserId, "user@example.com") : null);
        public Task RequestPasswordRecoveryAsync(string email, Uri? redirectTo = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Profiles(string role, bool active) : IUserProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(string id, string token, CancellationToken ct = default) =>
            Task.FromResult<Profile?>(new Profile(id, "user@example.com", "User", null, role,
                active ? "active" : "inactive", DateTimeOffset.UtcNow, null, null));
        public Task<Profile?> UpdateCurrentAsync(string id, string token, UpdateOwnProfile update, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class Conversations : IConversationRepository
    {
        public string? LastUser { get; private set; }
        public string? LastToken { get; private set; }
        public int LastPageSize { get; private set; }
        public Task<ChatSession?> GetSessionAsync(string userId, string token, string sessionId, CancellationToken ct = default)
        {
            LastUser = userId; LastToken = token;
            return Task.FromResult<ChatSession?>(null);
        }
        public Task<Page<ChatSession>> ListSessionsAsync(string id, string token, PageRequest page, bool includeDetails = false, CancellationToken ct = default)
        {
            LastPageSize = page.Limit;
            return Task.FromResult(new Page<ChatSession>(includeDetails
                ? [new ChatSession(SessionId, id, "New Chat", DateTimeOffset.UtcNow, 2, "hello")]
                : [], false));
        }
        public Task<ChatSession?> CreateSessionAsync(string id, string token, string? title, CancellationToken ct = default) => Task.FromResult<ChatSession?>(null);
        public Task<ChatSession?> UpdateSessionTitleAsync(string id, string token, string session, string title, CancellationToken ct = default) => Task.FromResult<ChatSession?>(null);
        public Task<bool> DeleteSessionAsync(string id, string token, string session, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string id, string token, string session, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task<IReadOnlyList<ChatMessage>> GetLastMessagesAsync(string id, string token, string session, int count, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task<ChatMessage?> InsertMessageAsync(string id, string token, CreateChatMessage message, CancellationToken ct = default) => Task.FromResult<ChatMessage?>(null);
        public Task<FeedbackMessageContext?> GetFeedbackMessageContextAsync(string id, string token, string message, CancellationToken ct = default) => Task.FromResult<FeedbackMessageContext?>(null);
    }
}

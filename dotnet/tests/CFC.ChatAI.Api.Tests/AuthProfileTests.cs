using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.Api.Application.Abstractions;
using CFC.ChatAI.Api.Application.Models;
using CFC.ChatAI.Api.Application.Services;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Infrastructure.Supabase;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.Api.Tests;

public sealed class AuthProfileTests
{
    private const string Row = "{\"id\":\"user-a\",\"email\":\"a@example.com\",\"full_name\":null,\"avatar_url\":null,\"role\":\"user\",\"status\":\"active\",\"created_at\":\"2026-03-17T09:00:00.123456+00:00\",\"deleted_at\":null}";
    private static SupabaseOptions Options(TimeSpan? timeout = null) => new()
    {
        Url = "https://example.supabase.co", AnonKey = "anon-key", ServiceRoleKey = "secret-key",
        FrontendBaseUrl = "https://frontend.example/ui?x=a&y=b///", RequestTimeout = timeout ?? TimeSpan.FromSeconds(2)
    };
    private static WebApplicationFactory<Program> Factory(Handler handler, SupabaseOptions? options = null,
        Action<IServiceCollection>? configure = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SupabaseOptions>();
            services.AddSingleton(options ?? Options());
            services.AddHttpClient(SupabaseRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            configure?.Invoke(services);
        }));
    private static HttpRequestMessage Request(string method, string path, string? body = null, string? token = "token-a")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return request;
    }
    private static Task<HttpResponseMessage> Json(string body, int status = 200) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    private static Task<HttpResponseMessage> Active(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath == "/auth/v1/user") return Json("{\"id\":\"user-a\"}");
        if (request.Headers.GetValues("apikey").Single() == "secret-key") return Json("[{\"status\":\"active\"}]");
        return Json("[" + Row + "]");
    }
    private static async Task AssertDetail(HttpResponseMessage response, int status, string detail)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(new { detail }), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(429, false)]
    [InlineData(400, true)]
    [InlineData(500, true)]
    [InlineData(302, true)]
    public async Task Recovery_ExactContract_Credentials_Redirect_NoRetry(int status, bool success)
    {
        using var handler = new Handler(async (r, ct) =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("/auth/v1/recover", r.RequestUri!.AbsolutePath);
            Assert.Equal("?redirect_to=https%3A%2F%2Ffrontend.example%2Fui%3Fx%3Da%26y%3Db", r.RequestUri.Query);
            Assert.Equal("secret-key", r.Headers.GetValues("apikey").Single());
            Assert.Equal("Bearer secret-key", r.Headers.Authorization!.ToString());
            Assert.Equal("{\"email\":\" not-an-email \"}", await r.Content!.ReadAsStringAsync(ct));
            return await Json("private-provider-body", status);
        });
        using var factory = Factory(handler);
        var response = await factory.CreateClient().PostAsync("/api/auth/forgot-password", new StringContent("{\"email\":\" not-an-email \",\"redirect_to\":\"evil\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(success, json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(success ? "If an account exists with this email, a password reset link has been sent." :
            "Too many reset attempts. Please wait a few minutes before trying again.", json.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("private-provider-body", body);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("", "missing", "Field required")]
    [InlineData("null", "missing", "Field required")]
    [InlineData("{}", "missing", "Field required")]
    [InlineData("{\"email\":null}", "string_type", "Input should be a valid string")]
    [InlineData("{\"email\":3}", "string_type", "Input should be a valid string")]
    [InlineData("{\"email\":false}", "string_type", "Input should be a valid string")]
    [InlineData("[]", "model_attributes_type", "Input should be a valid dictionary or object to extract fields from")]
    [InlineData("{", "json_invalid", "JSON decode error")]
    public async Task AuthValidation_422Arrays_NoProviderCall(string raw, string type, string message)
    {
        using var handler = new Handler((_, _) => throw new Exception("Unexpected provider call"));
        using var factory = Factory(handler);
        foreach (var route in new[] { "/api/auth/forgot-password", "/api/auth/check-email" })
        {
            var response = await factory.CreateClient().SendAsync(Request("POST", route, raw, null));
            Assert.Equal(422, (int)response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var error = json.RootElement.GetProperty("detail")[0];
            Assert.Equal(type, error.GetProperty("type").GetString());
            Assert.Equal(message, error.GetProperty("msg").GetString());
            Assert.Equal("body", error.GetProperty("loc")[0].GetString());
            Assert.True(error.TryGetProperty("input", out _));
        }
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidEmail_MatchesCapturedPythonError()
    {
        using var handler = new Handler((_, _) => throw new Exception());
        using var factory = Factory(handler);
        var response = await factory.CreateClient().SendAsync(Request("POST", "/api/auth/check-email", "{\"email\":\"bad\"}"));
        Assert.Equal(422, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("value is not a valid email address: An email address must have an @-sign.",
            json.RootElement.GetProperty("detail")[0].GetProperty("msg").GetString());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("[]", false, "This email has not been invited. Please contact your administrator.")]
    [InlineData("[{\"expires_at\":\"2026-04-01T00:00:00Z\",\"is_registered\":false}]", true, "Email is eligible for registration.")]
    [InlineData("[{\"expires_at\":\"2026-03-17T00:00:00Z\",\"is_registered\":false}]", false, "Your invitation has expired. Please contact your administrator for a new one.")]
    [InlineData("[{\"expires_at\":\"2026-03-17T01:00:00+01:00\",\"is_registered\":false}]", false, "Your invitation has expired. Please contact your administrator for a new one.")]
    [InlineData("[{\"expires_at\":\"2026-03-16T00:00:00Z\",\"is_registered\":false}]", false, "Your invitation has expired. Please contact your administrator for a new one.")]
    [InlineData("[{\"expires_at\":\"2026-04-01T00:00:00\",\"is_registered\":false}]", false, "Unable to verify email. Please try again later.")]
    [InlineData("[{\"expires_at\":\"garbage\",\"is_registered\":false}]", false, "Unable to verify email. Please try again later.")]
    [InlineData("[{}]", false, "Unable to verify email. Please try again later.")]
    [InlineData("[{\"expires_at\":\"2026-04-01T00:00:00Z\",\"is_registered\":true}]", false, "Unable to verify email. Please try again later.")]
    public async Task Eligibility_Query_Normalization_UtcAndFailureOutcomes(string rows, bool eligible, string message)
    {
        using var handler = new Handler((r, _) =>
        {
            Assert.Equal("Bearer secret-key", r.Headers.Authorization!.ToString());
            Assert.Equal("secret-key", r.Headers.GetValues("apikey").Single());
            Assert.Equal("/rest/v1/invitations?select=expires_at,is_registered&email=eq.%22User%40example.com%22&is_registered=eq.false&order=created_at.desc&limit=1", r.RequestUri!.PathAndQuery);
            return Json(rows);
        });
        using var factory = Factory(handler, configure: s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new FixedTime()); });
        var response = await factory.CreateClient().SendAsync(Request("POST", "/api/auth/check-email", "{\"email\":\" User@EXAMPLE.COM \"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(eligible, json.RootElement.GetProperty("eligible").GetBoolean());
        Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    public async Task MissingMalformedBearer_AllProfileRoutes(string? authorization)
    {
        using var handler = new Handler((_, _) => throw new Exception());
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes)
        {
            using var request = Request(method, path, "{}", null);
            if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
            await AssertDetail(await factory.CreateClient().SendAsync(request), 401, "Not authenticated");
        }
        Assert.Empty(handler.Requests);
    }

    private static readonly (string, string)[] Routes = [("GET", "/api/profile/me"), ("PATCH", "/api/profile/me"), ("POST", "/api/profile/complete")];

    [Theory]
    [InlineData("[]", 403, "User profile not found")]
    [InlineData("[{\"status\":\"inactive\"}]", 403, "Account is inactive")]
    [InlineData("[{\"status\":\"deleted\"}]", 403, "Account has been deleted")]
    [InlineData("[{\"status\":\"pending\"}]", 403, "Account is not active")]
    [InlineData("[{\"status\":null}]", 403, "Account is not active")]
    [InlineData("[{}]", 403, "Account is not active")]
    [InlineData("[{\"status\":3}]", 503, "Account verification unavailable")]
    [InlineData("garbage", 503, "Account verification unavailable")]
    public async Task AccessStateMatrix_AllProfileRoutes_NoSelfOperations(string rows, int status, string detail)
    {
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath == "/auth/v1/user")
            {
                Assert.Equal("Bearer token-a", r.Headers.Authorization!.ToString());
                Assert.Equal("secret-key", r.Headers.GetValues("apikey").Single());
                return Json("{\"id\":\"user-a\"}");
            }
            Assert.Equal("/rest/v1/profiles?select=status&id=eq.%22user-a%22&limit=1", r.RequestUri.PathAndQuery);
            Assert.Equal("Bearer secret-key", r.Headers.Authorization!.ToString());
            return Json(rows);
        });
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes)
            await AssertDetail(await factory.CreateClient().SendAsync(Request(method, path, "{\"full_name\":\"x\"}")), status, detail);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401, "{}", 401, "Invalid token")]
    [InlineData(403, "{}", 401, "Invalid token")]
    [InlineData(200, "{}", 401, "Invalid token")]
    [InlineData(200, "{\"id\":null}", 401, "Invalid token")]
    [InlineData(200, "{\"id\":3}", 503, "Authentication service unavailable")]
    [InlineData(200, "garbage", 503, "Authentication service unavailable")]
    [InlineData(500, "private-upstream-body", 503, "Authentication service unavailable")]
    [InlineData(429, "private-upstream-body", 503, "Authentication service unavailable")]
    public async Task AuthIdentity_InvalidVersusUnavailable(int providerStatus, string body, int status, string detail)
    {
        using var handler = new Handler((_, _) => Json(body, providerStatus));
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes)
            await AssertDetail(await factory.CreateClient().SendAsync(Request(method, path, "{}")), status, detail);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadProfile_ExactProjection_PreservesNullsAndTimestamp_RlsHeaders()
    {
        using var handler = new Handler((r, ct) =>
        {
            if (r.Headers.GetValues("apikey").Single() == "anon-key")
            {
                Assert.Equal("Bearer token-a", r.Headers.Authorization!.ToString());
                Assert.Equal("/rest/v1/profiles?select=id,email,full_name,avatar_url,role,status,created_at&id=eq.%22user-a%22&limit=1", r.RequestUri!.PathAndQuery);
            }
            return Active(r, ct);
        });
        using var factory = Factory(handler);
        var response = await factory.CreateClient().SendAsync(Request("GET", "/api/profile/me?id=other"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(7, root.EnumerateObject().Count());
        Assert.Equal("user-a", root.GetProperty("id").GetString());
        Assert.Equal("a@example.com", root.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("full_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("avatar_url").ValueKind);
        Assert.Equal("2026-03-17T09:00:00.123456+00:00", root.GetProperty("created_at").GetString());
        Assert.False(root.TryGetProperty("deleted_at", out _));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{\"full_name\":\"New Name\"}", "{\"full_name\":\"New Name\"}")]
    [InlineData("{\"avatar_url\":\"arbitrary string\"}", "{\"avatar_url\":\"arbitrary string\"}")]
    [InlineData("{\"full_name\":\"\",\"avatar_url\":\"\"}", "{\"full_name\":\"\",\"avatar_url\":\"\"}")]
    [InlineData("{\"full_name\":\"Name\",\"avatar_url\":null}", "{\"full_name\":\"Name\"}")]
    [InlineData("{\"full_name\":null,\"avatar_url\":\"url\"}", "{\"avatar_url\":\"url\"}")]
    [InlineData("{\"full_name\":\"x\",\"id\":\"other\",\"role\":\"admin\",\"status\":\"active\",\"email\":\"other\",\"deleted_at\":\"x\",\"created_at\":\"x\"}", "{\"full_name\":\"x\"}")]
    public async Task UpdateAndCompletion_AllowlistAndNullSemantics(string input, string expected)
    {
        using var handler = new Handler(async (r, ct) =>
        {
            if (r.Method == HttpMethod.Patch)
            {
                Assert.Equal("anon-key", r.Headers.GetValues("apikey").Single());
                Assert.Equal("Bearer token-a", r.Headers.Authorization!.ToString());
                Assert.Equal("return=representation", r.Headers.GetValues("Prefer").Single());
                Assert.EndsWith("&id=eq.%22user-a%22", r.RequestUri!.PathAndQuery);
                Assert.Equal(expected, await r.Content!.ReadAsStringAsync(ct));
            }
            return await Active(r, ct);
        });
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes.Skip(1))
        {
            var response = await factory.CreateClient().SendAsync(Request(method, path, input));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(6, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{}", 400)]
    [InlineData("{\"full_name\":null,\"avatar_url\":null}", 400)]
    [InlineData("{\"role\":\"admin\"}", 400)]
    [InlineData("{\"full_name\":3}", 422)]
    [InlineData("{\"avatar_url\":false}", 422)]
    [InlineData("null", 422)]
    [InlineData("{", 422)]
    public async Task InvalidPatch_NoWrites(string input, int status)
    {
        using var handler = new Handler(Active);
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes.Skip(1))
        {
            var response = await factory.CreateClient().SendAsync(Request(method, path, input));
            Assert.Equal(status, (int)response.StatusCode);
            if (status == 400) await AssertDetail(response, 400, "No fields to update. Provide full_name or avatar_url.");
        }
        Assert.Equal(4, handler.Requests.Count);
    }

    [Theory]
    [InlineData("[]", 200, 404)]
    [InlineData("[{}]", 200, 500)]
    [InlineData("garbage", 200, 500)]
    [InlineData("private-token-body", 403, 500)]
    [InlineData("{}", 200, 500)]
    public async Task SelfProviderFailures_AreSafe_NoPrivilegedFallback(string body, int upstream, int getStatus)
    {
        using var handler = new Handler((r, ct) => r.Headers.GetValues("apikey").Single() == "anon-key"
            ? Json(body, upstream) : Active(r, ct));
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes)
        {
            var response = await factory.CreateClient().SendAsync(Request(method, path, "{\"full_name\":\"x\"}"));
            await AssertDetail(response, method == "GET" ? getStatus : 500,
                method == "GET" ? getStatus == 404 ? "Profile not found" : "Failed to get profile" : "Failed to update profile");
        }
        Assert.Equal(9, handler.Requests.Count);
    }

    [Fact]
    public async Task Deactivation_CheckedEveryRequest()
    {
        var checks = 0;
        using var handler = new Handler((r, ct) =>
        {
            if (r.RequestUri!.Query.StartsWith("?select=status"))
                return Json(Interlocked.Increment(ref checks) == 1 ? "[{\"status\":\"active\"}]" : "[{\"status\":\"inactive\"}]");
            return Active(r, ct);
        });
        using var factory = Factory(handler);
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("GET", "/api/profile/me"))).StatusCode);
        await AssertDetail(await client.SendAsync(Request("PATCH", "/api/profile/me", "{\"full_name\":\"x\"}")), 403, "Account is inactive");
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentTokens_CannotBleed_IdentityFilterIgnoresCallerId()
    {
        using var handler = new Handler(async (r, ct) =>
        {
            await Task.Delay(5, ct);
            var token = r.Headers.Authorization!.Parameter;
            if (r.RequestUri!.AbsolutePath == "/auth/v1/user") return await Json("{\"id\":\"" + token + "\"}");
            if (r.Headers.GetValues("apikey").Single() == "secret-key") return await Json("[{\"status\":\"active\"}]");
            Assert.Contains("id=eq.%22" + token + "%22", r.RequestUri.Query);
            return await Json("[" + Row.Replace("user-a", token) + "]");
        });
        using var factory = Factory(handler);
        var client = factory.CreateClient();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
        {
            var token = "user-" + index;
            var response = await client.SendAsync(Request("GET", "/api/profile/me?id=victim", token: token));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(token, json.RootElement.GetProperty("id").GetString());
        }));
        Assert.Equal(60, handler.Requests.Count);
    }

    [Fact]
    public async Task FilterEncoding_ReservedCharactersCannotBroadenQuery()
    {
        const string id = "a\",id.neq.victim)&limit=100";
        using var handler = new Handler((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath == "/auth/v1/user") return Json(JsonSerializer.Serialize(new { id }));
            Assert.Contains("id=eq.%22a%5C%22%2Cid.neq.victim%29%26limit%3D100%22", r.RequestUri.Query);
            if (r.Headers.GetValues("apikey").Single() == "secret-key") return Json("[{\"status\":\"active\"}]");
            return Json("[" + Row.Replace("\"user-a\"", JsonSerializer.Serialize(id)) + "]");
        });
        using var factory = Factory(handler);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(Request("GET", "/api/profile/me"))).StatusCode);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("transport")]
    [InlineData("unconfigured")]
    public async Task DependencyFailure_PublicSafe_ProtectedFailClosed(string failure)
    {
        using var handler = new Handler(async (_, ct) =>
        {
            if (failure == "transport") throw new HttpRequestException("secret-key token-a private-body");
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return await Json("{}");
        });
        using var factory = Factory(handler, failure == "unconfigured" ? new() : Options(TimeSpan.FromMilliseconds(30)));
        var client = factory.CreateClient();
        var response = await client.SendAsync(Request("POST", "/api/auth/forgot-password", "{\"email\":\"a@example.com\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"success\":true", await response.Content.ReadAsStringAsync());
        response = await client.SendAsync(Request("POST", "/api/auth/check-email", "{\"email\":\"a@example.com\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Unable to verify email. Please try again later.", await response.Content.ReadAsStringAsync());
        await AssertDetail(await client.SendAsync(Request("GET", "/api/profile/me")), 503, "Authentication service unavailable");
        if (failure == "unconfigured") Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Cancellation_IsNotConvertedToSuccess()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return await Json("{}"); });
        using var factory = Factory(handler);
        var gateway = factory.Services.GetRequiredService<IIdentityGateway>();
        using var cts = new CancellationTokenSource();
        var task = gateway.RecoverAsync("a@example.com", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData("http://example.supabase.co")]
    [InlineData("https://user:password@example.supabase.co")]
    [InlineData("https://example.supabase.co?query=1")]
    [InlineData("https://example.supabase.co#fragment")]
    public async Task UnsafeBaseUrl_NoCredentialsSent(string url)
    {
        using var handler = new Handler((_, _) => throw new Exception());
        using var factory = Factory(handler, new() { Url = url, AnonKey = "anon-key", ServiceRoleKey = "secret-key" });
        await AssertDetail(await factory.CreateClient().SendAsync(Request("GET", "/api/profile/me")), 503, "Authentication service unavailable");
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LocalRecoveryAbuseLimit_HasExplicit429_NotProvider429Contract()
    {
        using var handler = new Handler((_, _) => Json("{}"));
        using var factory = Factory(handler);
        var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("POST", "/api/auth/forgot-password", "{\"email\":\"anything\"}"))).StatusCode);
        await AssertDetail(await client.SendAsync(Request("POST", "/api/auth/forgot-password", "{\"email\":\"anything\"}")), 429,
            "Too many requests. Please try again later.");
        Assert.Equal(10, handler.Requests.Count);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/config")).StatusCode);
    }

    [Fact]
    public async Task StatusDependencyUnavailable_DoesNotReachSelfOperations()
    {
        using var handler = new Handler((r, _) => r.RequestUri!.AbsolutePath == "/auth/v1/user"
            ? Json("{\"id\":\"user-a\"}") : Json("secret-provider-body", 500));
        using var factory = Factory(handler);
        foreach (var (method, path) in Routes)
            await AssertDetail(await factory.CreateClient().SendAsync(Request(method, path, "{\"full_name\":\"x\"}")), 503, "Account verification unavailable");
        Assert.Equal(6, handler.Requests.Count);
    }

    [Theory]
    [InlineData("secret-key")]
    [InlineData("sb_secret_accidental")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.signature")]
    public async Task SelfOperation_RejectsPrivilegedAnonConfiguration(string anon)
    {
        using var handler = new Handler(Active);
        using var factory = Factory(handler, new() { Url = "https://example.supabase.co", ServiceRoleKey = "secret-key", AnonKey = anon });
        await AssertDetail(await factory.CreateClient().SendAsync(Request("GET", "/api/profile/me")), 500, "Failed to get profile");
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class FixedTime : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-03-17T00:00:00Z"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests.Enqueue(request.RequestUri!.ToString()); return respond(request, cancellationToken); }
    }
}

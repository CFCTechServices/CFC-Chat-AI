using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CFC.ChatAI.Api.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.Api.Tests;

public sealed class SupabaseAuthorizationFactAttribute : FactAttribute
{
    public SupabaseAuthorizationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_SUPABASE_AUTH_TESTS") != "1")
            Skip = "Set RUN_SUPABASE_AUTH_TESTS=1 and supply an isolated test project with two disposable active users.";
    }
}

/// <summary>Mutates disposable test profiles. Never enable against a production project.</summary>
public sealed class SupabaseAuthorizationIntegrationTests
{
    [SupabaseAuthorizationFact]
    [Trait("Category", "Integration")]
    public async Task LiveRls_ColumnProtection_AndImmediateDeactivation()
    {
        static string Env(string key) => Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException("Required test setting is missing: " + key);
        var url = Env("TEST_SUPABASE_URL").TrimEnd('/');
        var anon = Env("TEST_SUPABASE_ANON_KEY");
        var service = Env("TEST_SUPABASE_SERVICE_ROLE_KEY");
        var tokenA = Env("TEST_SUPABASE_USER_A_TOKEN");
        var tokenB = Env("TEST_SUPABASE_USER_B_TOKEN");
        var invitedEmail = Env("TEST_SUPABASE_INVITATION_EMAIL");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("TEST_SUPABASE_URL must be a clean HTTPS URL.");
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        async Task<HttpResponseMessage> Send(string path, string key, string token, HttpMethod? method = null, object? body = null)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url + path);
            request.Headers.Add("apikey", key);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Prefer", "return=representation");
            if (body is not null) request.Content = JsonContent.Create(body);
            return await http.SendAsync(request);
        }
        async Task<JsonElement> Get(string path, string key, string token)
        {
            using var response = await Send(path, key, token);
            Assert.True(response.IsSuccessStatusCode, "Live provider request failed; body intentionally omitted.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
        var idA = (await Get("/auth/v1/user", anon, tokenA)).GetProperty("id").GetString()!;
        var idB = (await Get("/auth/v1/user", anon, tokenB)).GetProperty("id").GetString()!;
        Assert.NotEqual(idA, idB);
        string Filter(string id) => "/rest/v1/profiles?id=" + Uri.EscapeDataString("eq.\"" + id + "\"");
        var originalA = (await Get(Filter(idA) + "&select=*", service, service))[0];
        var originalB = (await Get(Filter(idB) + "&select=*", service, service))[0];
        Assert.Equal("active", originalA.GetProperty("status").GetString());
        Assert.Equal("active", originalB.GetProperty("status").GetString());
        async Task Patch(string id, object body)
        {
            using var response = await Send(Filter(id), service, service, HttpMethod.Patch, body);
            Assert.True(response.IsSuccessStatusCode, "Privileged test patch failed.");
        }
        try
        {
            foreach (var (token, otherId) in new[] { (tokenA, idB), (tokenB, idA) })
            {
                var hidden = await Get(Filter(otherId) + "&select=id", anon, token);
                Assert.Equal(0, hidden.GetArrayLength());
                using var crossUpdate = await Send(Filter(otherId), anon, token, HttpMethod.Patch, new { full_name = "must-not-change" });
                if (crossUpdate.IsSuccessStatusCode)
                {
                    using var rows = JsonDocument.Parse(await crossUpdate.Content.ReadAsStringAsync());
                    Assert.Equal(0, rows.RootElement.GetArrayLength());
                }
            }
            // Direct PostgREST calls must protect sensitive columns too, not just this API's allowlist.
            foreach (var field in new[] { "role", "status", "email", "id", "deleted_at", "created_at" })
            {
                object? value = field switch
                {
                    "role" => "admin", "status" => "inactive", "email" => "forbidden-change@example.com",
                    "id" => Guid.NewGuid().ToString(), "deleted_at" => "2026-03-17T00:00:00Z",
                    _ => "2026-03-17T00:00:00Z"
                };
                using var attempt = await Send(Filter(idA), anon, tokenA, HttpMethod.Patch, new Dictionary<string, object?> { [field] = value });
                var after = await Get(Filter(idA) + "&select=*", service, service);
                if (field == "id" && after.GetArrayLength() == 0)
                {
                    // Repair an identity-column policy defect before reporting it.
                    using var repair = await Send(Filter((string)value!), service, service, HttpMethod.Patch, new { id = idA });
                    Assert.True(repair.IsSuccessStatusCode, "Identity restoration failed; manually clean up the disposable user.");
                    Assert.Fail("User-token PostgREST allowed an identity-column change.");
                }
                Assert.Equal(1, after.GetArrayLength());
                Assert.Equal(originalA.GetProperty(field).GetRawText(), after[0].GetProperty(field).GetRawText());
            }
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<SupabaseOptions>();
                services.AddSingleton(new SupabaseOptions { Url = url, AnonKey = anon, ServiceRoleKey = service });
            }));
            var client = factory.CreateClient();
            async Task<HttpResponseMessage> Api(string token, HttpMethod method, string path, object? body = null)
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body is not null) request.Content = JsonContent.Create(body);
                return await client.SendAsync(request);
            }
            foreach (var (token, id) in new[] { (tokenA, idA), (tokenB, idB) })
            {
                using var response = await Api(token, HttpMethod.Get, "/api/profile/me?id=" + (id == idA ? idB : idA));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var row = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(id, row.RootElement.GetProperty("id").GetString());
                using var update = await Api(token, HttpMethod.Patch, "/api/profile/me", new { full_name = "disposable-auth-test" });
                Assert.Equal(HttpStatusCode.OK, update.StatusCode);
                Assert.Equal("disposable-auth-test", (await Get(Filter(id) + "&select=full_name", service, service))[0].GetProperty("full_name").GetString());
            }
            await Patch(idA, new { status = "inactive" });
            foreach (var (method, path) in new[] { (HttpMethod.Get, "/api/profile/me"), (HttpMethod.Patch, "/api/profile/me"), (HttpMethod.Post, "/api/profile/complete") })
            {
                using var rejected = await Api(tokenA, method, path, new { full_name = "must-not-change" });
                Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            }
            Assert.Equal("disposable-auth-test", (await Get(Filter(idA) + "&select=full_name", service, service))[0].GetProperty("full_name").GetString());
            // Require a seeded row so a legitimately empty table cannot produce a false pass.
            var invitationPath = "/rest/v1/invitations?select=email&email="
                + Uri.EscapeDataString("eq.\"" + invitedEmail.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"") + "&limit=1";
            Assert.Equal(1, (await Get(invitationPath, service, service)).GetArrayLength());
            // Invitation rows must not be readable with either public or user credentials.
            foreach (var token in new[] { anon, tokenA, tokenB })
            {
                using var response = await Send(invitationPath, anon, token);
                if (response.IsSuccessStatusCode)
                {
                    using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.Equal(0, rows.RootElement.GetArrayLength());
                }
            }
        }
        finally
        {
            // Restore all mutable columns if a policy defect allowed a forbidden write.
            foreach (var (id, original) in new[] { (idA, originalA), (idB, originalB) })
            {
                var restore = new Dictionary<string, object?>();
                foreach (var field in new[] { "id", "email", "role", "status", "deleted_at", "created_at", "full_name", "avatar_url" })
                    if (original.TryGetProperty(field, out var value)) restore[field] = value;
                await Patch(id, restore);
            }
        }
    }
}

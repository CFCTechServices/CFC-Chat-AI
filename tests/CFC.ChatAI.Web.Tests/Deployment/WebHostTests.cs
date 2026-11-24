using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Services.Api;
using CFC.ChatAI.Web.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CFC.ChatAI.Web.Tests.Deployment;

public sealed class WebHostTests
{
    [Fact]
    public async Task RegisteredClientCallsTheRealDotNetBackendOverLoopback()
    {
        await using var api = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:Urls", "http://127.0.0.1:0");
            builder.UseSetting("SUPABASE_URL", "https://fixture.supabase.co");
            builder.UseSetting("SUPABASE_ANON_KEY", "sb_publishable_fixture");
            builder.UseSetting("SUPABASE_SERVICE_ROLE_KEY", "service-fixture");
            builder.UseSetting("PINECONE_API_KEY", ""); // Never call a live provider.
        });
        api.UseKestrel(0);
        using var apiHttp = api.CreateClient();
        await using var web = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Backend:BaseUrl", apiHttp.BaseAddress!.AbsoluteUri));
        using var scope = web.Services.CreateScope();
        var backend = scope.ServiceProvider.GetRequiredService<IAuthenticationBackend>();
        var client = Assert.IsType<ChatApiClient>(backend);

        Assert.True((await client.GetHealthAsync(default)).Ok);
        var config = await client.GetAuthConfigAsync();
        Assert.Equal("dotnet", config.Backend);
        Assert.Equal("https://fixture.supabase.co", config.SupabaseUrl);
        Assert.Equal("sb_publishable_fixture", config.SupabaseKey);
        Assert.Equal("false", config.DocumentUploads);
    }

    [Theory]
    [InlineData("/", "/login")]
    [InlineData("/ui", "/login")]
    [InlineData("/ui/?signup=return", "/login?signup=return")]
    [InlineData("/docs", "/chat")]
    public async Task RootAndLegacyEntryLinksRedirectToBlazor(string from, string to)
    {
        await using var web = new WebApplicationFactory<Program>();
        using var browser = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await browser.GetAsync(from);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(to, new Uri(browser.BaseAddress!, response.Headers.Location!).PathAndQuery);
    }

    [Fact]
    public async Task BlazorServesAssetsDeepLinksAndTheSignalRCircuitEndpoint()
    {
        await using var web = new WebApplicationFactory<Program>();
        using var browser = web.CreateClient();
        var html = await browser.GetStringAsync("/login");
        Assert.Contains("<base href=\"/\"", html);
        Assert.DoesNotContain(".jsx", html);
        var script = Regex.Match(html, "src=\"([^\"]*blazor\\.web[^\"]*\\.js)\"");
        Assert.True(script.Success);
        using var asset = await browser.GetAsync(script.Groups[1].Value);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("javascript", asset.Content.Headers.ContentType!.MediaType);
        foreach (var page in new[] { "/chat/session-id", "/admin", "/history", "/settings", "/reset-password" })
        {
            using var reply = await browser.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        }
        using var negotiate = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", content: null);
        negotiate.EnsureSuccessStatusCode();
        var payload = await negotiate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("connectionToken").GetString()));
    }

    [Theory]
    [InlineData("/unknown")]
    [InlineData("/_framework/missing.js")]
    [InlineData("/ui/main.jsx")]
    [InlineData("/api/health/detailed")]
    public async Task MissingPagesAssetsAndApiCallsDoNotFallBackToSuccess(string path)
    {
        await using var web = new WebApplicationFactory<Program>();
        using var browser = web.CreateClient();
        using var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.Api.Tests;

public sealed class RuntimeDiagnosticsTests
{
    private static RuntimeOptions Configured(TimeSpan? timeout = null) => new()
    {
        PineconeApiKey = "pinecone-private", PineconeIndexName = "DocumentIndex",
        SupabaseUrl = "https://example.supabase.co", SupabaseAnonKey = "public-anon",
        SupabaseServiceRoleKey = "supabase-private", AzureApiKey = "azure-private",
        AzureEndpoint = "https://example.openai.azure.com", AzureDeployment = "gpt-test",
        RequestTimeout = timeout ?? TimeSpan.FromSeconds(2)
    };

    private static WebApplicationFactory<Program> Factory(RuntimeOptions options, Handler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<RuntimeOptions>();
            services.AddSingleton(options);
            services.AddHttpClient(RuntimeDiagnostics.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        }));

    [Fact]
    public async Task AuthConfig_UsesExactCamelCase_AndOnlyPublicKey()
    {
        using var handler = new Handler((_, _) => throw new Exception("No network expected"));
        using var factory = Factory(Configured(), handler);
        var body = await factory.CreateClient().GetStringAsync("/api/auth/config");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal("https://example.supabase.co", json.RootElement.GetProperty("supabaseUrl").GetString());
        Assert.Equal("public-anon", json.RootElement.GetProperty("supabaseKey").GetString());
        AssertNoSecrets(body);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthConfig_MissingConfiguration_EmitsNulls()
    {
        using var factory = Factory(new(), new Handler((_, _) => throw new Exception()));
        using var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/api/auth/config"));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("supabaseUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("supabaseKey").ValueKind);
    }

    [Theory]
    [InlineData("supabase-private")]
    [InlineData("sb_secret_mistaken-anon")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.signature")]
    public async Task AuthConfig_RejectsPrivilegedKeysAccidentallyConfiguredAsAnon(string key)
    {
        using var factory = Factory(new() { SupabaseAnonKey = key, SupabaseServiceRoleKey = "supabase-private" }, new Handler((_, _) => throw new Exception()));
        using var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/api/auth/config"));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("supabaseKey").ValueKind);
    }

    [Fact]
    public async Task DetailedHealth_Healthy_HasPythonFields_AndCorrectRequests()
    {
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.Host == "api.pinecone.io")
            {
                Assert.Equal("pinecone-private", request.Headers.GetValues("Api-Key").Single());
                Assert.Equal("2025-10", request.Headers.GetValues("X-Pinecone-API-Version").Single());
                Assert.Equal("/indexes", request.RequestUri.AbsolutePath);
                return Json("{\"indexes\":[]}");
            }
            Assert.Equal("example.supabase.co", request.RequestUri.Host);
            Assert.Equal("/rest/v1/profiles?select=id&limit=1", request.RequestUri.PathAndQuery);
            Assert.Equal("supabase-private", request.Headers.GetValues("apikey").Single());
            Assert.Equal("Bearer supabase-private", request.Headers.Authorization!.ToString());
            return Json("[]");
        });
        using var factory = Factory(Configured(), handler);
        var body = await factory.CreateClient().GetStringAsync("/api/health/detailed");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.Equal("All systems operational", root.GetProperty("message").GetString());
        var services = root.GetProperty("services");
        Assert.Equal(4, services.EnumerateObject().Count());
        Assert.All(services.EnumerateObject(), s => Assert.Equal("ok", s.Value.GetProperty("status").GetString()));
        Assert.Equal("DocumentIndex", services.GetProperty("pinecone").GetProperty("index").GetString());
        Assert.Equal("https://example.supabase.co", services.GetProperty("supabase").GetProperty("url").GetString());
        Assert.Equal("gpt-test", services.GetProperty("azure_openai").GetProperty("deployment").GetString());
        Assert.Equal("sentence-transformers/all-MiniLM-L6-v2", services.GetProperty("embedding_model").GetProperty("model").GetString());
        Assert.False(services.GetProperty("pinecone").TryGetProperty("detail", out _));
        Assert.Equal(2, handler.Requests.Count); // no paid AI call or model download
        AssertNoSecrets(body);
    }

    [Fact]
    public async Task DetailedHealth_Unconfigured_DoesNotMakeExternalRequests()
    {
        using var handler = new Handler((_, _) => throw new Exception("No network expected"));
        using var factory = Factory(new(), handler);
        using var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/api/health/detailed"));
        var root = json.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("One or more services degraded", root.GetProperty("message").GetString());
        var services = root.GetProperty("services");
        Assert.Equal("PINECONE_API_KEY not set", services.GetProperty("pinecone").GetProperty("detail").GetString());
        Assert.Equal("SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY not set", services.GetProperty("supabase").GetProperty("detail").GetString());
        foreach (var name in new[] { "pinecone", "supabase", "azure_openai" })
            Assert.Equal("not_configured", services.GetProperty(name).GetProperty("status").GetString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DetailedHealth_AzureDeploymentMissing_IsDegraded()
    {
        using var factory = Factory(new() { AzureApiKey = "azure-private", AzureEndpoint = "https://example.openai.azure.com" }, new Handler((_, _) => throw new Exception()));
        using var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/api/health/detailed"));
        var azure = json.RootElement.GetProperty("services").GetProperty("azure_openai");
        Assert.Equal("degraded", azure.GetProperty("status").GetString());
        Assert.Equal("AZURE_OPENAI_DEPLOYMENT not set — LLM disabled", azure.GetProperty("detail").GetString());
        Assert.False(azure.TryGetProperty("deployment", out _));
    }

    [Fact]
    public async Task DetailedHealth_Failures_AreSanitized_AndStillReturn200()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("pinecone-private supabase-private azure-private"));
        using var factory = Factory(Configured(), handler);
        var response = await factory.CreateClient().GetAsync("/api/health/detailed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertNoSecrets(body);
        using var json = JsonDocument.Parse(body);
        var services = json.RootElement.GetProperty("services");
        Assert.Equal("error", services.GetProperty("pinecone").GetProperty("status").GetString());
        Assert.Equal("Supabase health check failed", services.GetProperty("supabase").GetProperty("detail").GetString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("/api/health/detailed", HttpStatusCode.OK)]
    [InlineData("/api/visibility/vector-store", HttpStatusCode.InternalServerError)]
    public async Task ExternalChecks_AreBoundedByTimeout(string route, HttpStatusCode expected)
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return await Json("{}"); });
        using var factory = Factory(Configured(TimeSpan.FromMilliseconds(50)), handler);
        var response = await factory.CreateClient().GetAsync(route).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (route.Contains("health")) Assert.Contains("health check timed out", body);
        else Assert.Equal("{\"detail\":\"Failed to fetch vector store stats\"}", body);
        AssertNoSecrets(body);
    }

    [Fact]
    public async Task RequestCancellation_IsNotConvertedToDependencyFailure()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return await Json("{}"); });
        using var factory = Factory(Configured(), handler);
        var diagnostics = factory.Services.GetRequiredService<RuntimeDiagnostics>();
        using var cancellation = new CancellationTokenSource();
        var health = diagnostics.GetHealthAsync("test", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => health);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostics.GetVectorStatsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Visibility_Unconfigured_ReturnsSafe500WithoutNetwork()
    {
        using var handler = new Handler((_, _) => throw new Exception("No network expected"));
        using var factory = Factory(new(), handler);
        var response = await factory.CreateClient().GetAsync("/api/visibility/vector-store");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"detail\":\"Failed to fetch vector store stats\"}", await response.Content.ReadAsStringAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Visibility_ReadsExistingIndex_AndReturnsPythonContract()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("pinecone-private", request.Headers.GetValues("Api-Key").Single());
            if (request.RequestUri!.Host == "api.pinecone.io")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/indexes/DocumentIndex", request.RequestUri.AbsolutePath);
                return Json("{\"host\":\"docs-test.svc.pinecone.io\"}");
            }
            Assert.Equal("docs-test.svc.pinecone.io", request.RequestUri.Host);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/describe_index_stats", request.RequestUri.AbsolutePath);
            return Json("{\"totalVectorCount\":12,\"dimension\":384,\"indexFullness\":0.25,\"namespaces\":{\"\":{\"vectorCount\":10},\"MixedCase-NS\":{\"vectorCount\":2}}}");
        });
        using var factory = Factory(Configured(), handler);
        var response = await factory.CreateClient().GetAsync("/api/visibility/vector-store");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(7, root.EnumerateObject().Count());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("DocumentIndex", root.GetProperty("index_name").GetString());
        Assert.Equal(12, root.GetProperty("total_vectors").GetInt64());
        Assert.Equal(384, root.GetProperty("dimension").GetInt32());
        Assert.Equal(0.25, root.GetProperty("index_fullness").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal("default", root.GetProperty("namespaces")[0].GetProperty("name").GetString());
        Assert.Equal("MixedCase-NS", root.GetProperty("namespaces")[1].GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("namespaces")[1].GetProperty("vector_count").GetInt64());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{\"total_vector_count\":0,\"totalVectorCount\":99,\"namespaces\":{\"x\":{\"vectorCount\":3}}}", 0)]
    [InlineData("{\"total_vector_count\":null,\"totalVectorCount\":0}", 0)]
    [InlineData("{\"namespaces\":{\"\":{\"vectorCount\":3},\"x\":{\"vectorCount\":2},\"missing\":{}}}", 5)]
    [InlineData("{\"namespaces\":null}", 0)]
    [InlineData("{}", 0)]
    public void StatsMapping_PreservesExplicitZero_AndFallsBackToNamespaceSum(string body, long total)
    {
        using var json = JsonDocument.Parse(body);
        var stats = RuntimeDiagnostics.MapVectorStats(json.RootElement, "test");
        Assert.Equal(total, stats.TotalVectors);
        Assert.Null(stats.Dimension);
        Assert.Null(stats.IndexFullness);
    }

    [Theory]
    [InlineData("{\"indexFullness\":0,\"index_fullness\":0}", 0)]
    [InlineData("{\"index_fullness\":0.5}", 0.5)]
    public void StatsMapping_AcceptsSnakeCaseFullness(string body, double expected)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Equal(expected, RuntimeDiagnostics.MapVectorStats(json.RootElement, "test").IndexFullness);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("https://valid.svc.pinecone.io@attacker.example")]
    [InlineData("http://valid.svc.pinecone.io")]
    [InlineData("https://valid.svc.pinecone.io/path")]
    [InlineData("https://valid.svc.pinecone.io?secret=pinecone-private")]
    public async Task Visibility_DoesNotForwardKeyToUnsafeDiscoveredHost(string host)
    {
        using var handler = new Handler((_, _) => Json(JsonSerializer.Serialize(new { host })));
        using var factory = Factory(Configured(), handler);
        var response = await factory.CreateClient().GetAsync("/api/visibility/vector-store");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"detail\":\"Failed to fetch vector store stats\"}", await response.Content.ReadAsStringAsync());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Visibility_UpstreamFailureBody_IsNotExposed()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("pinecone-private") }));
        using var factory = Factory(Configured(), handler);
        var response = await factory.CreateClient().GetAsync("/api/visibility/vector-store");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"detail\":\"Failed to fetch vector store stats\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Configuration_MapsLegacyAndHierarchicalKeys_WithHierarchicalPrecedence()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PINECONE_API_KEY"] = "legacy", ["Pinecone:ApiKey"] = "hierarchical",
            ["PINECONE_INDEX_NAME"] = "docs", ["SUPABASE_URL"] = "https://example.supabase.co",
            ["SUPABASE_ANON_KEY"] = "anon", ["SUPABASE_SERVICE_ROLE_KEY"] = "private",
            ["AZURE_OPENAI_API_KEY"] = "azure", ["AZURE_OPENAI_ENDPOINT"] = "endpoint",
            ["AZURE_OPENAI_DEPLOYMENT"] = "deployment", ["EMBED_MODEL_NAME"] = "model",
            ["Diagnostics:RequestTimeoutSeconds"] = "0.25"
        }).Build();
        var options = RuntimeOptions.FromConfiguration(configuration);
        Assert.Equal("hierarchical", options.PineconeApiKey);
        Assert.Equal("docs", options.PineconeIndexName);
        Assert.Equal("https://example.supabase.co", options.SupabaseUrl);
        Assert.Equal("anon", options.SupabaseAnonKey);
        Assert.Equal("private", options.SupabaseServiceRoleKey);
        Assert.Equal("azure", options.AzureApiKey);
        Assert.Equal("endpoint", options.AzureEndpoint);
        Assert.Equal("deployment", options.AzureDeployment);
        Assert.Equal("model", options.EmbeddingModelName);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.RequestTimeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("61")]
    [InlineData("NaN")]
    public void Configuration_RejectsUnboundedTimeouts(string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostics:RequestTimeoutSeconds"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => RuntimeOptions.FromConfiguration(config));
    }

    private static void AssertNoSecrets(string body)
    {
        foreach (var secret in new[] { "pinecone-private", "supabase-private", "azure-private" }) Assert.DoesNotContain(secret, body);
    }

    private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!.ToString());
            return respond(request, cancellationToken);
        }
    }
}

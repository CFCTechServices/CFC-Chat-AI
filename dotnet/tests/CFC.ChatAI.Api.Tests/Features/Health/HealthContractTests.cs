using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CFC.ChatAI.Api.Tests.Features.Health;

public sealed class HealthContractTests
{
    [Fact]
    public async Task GetHealth_MatchesPythonResponseContract()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            "{\"ok\":true,\"message\":\"CFC Animal Feed Software Chatbot API is running\",\"version\":\"1.0.0\"}",
            body);
    }

    [Fact]
    public async Task GetHealth_UsesConfiguredVersion()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Api:Version"] = "2.3.4"
                })));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"version\":\"2.3.4\"", body);
    }

    [Fact]
    public async Task GetDetailedHealth_ReportsServiceChecksAndConfiguredVersion()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Api:Version"] = "2.3.4",
                    ["PINECONE_API_KEY"] = "",
                    ["SUPABASE_URL"] = "",
                    ["SUPABASE_SERVICE_ROLE_KEY"] = "",
                    ["AZURE_OPENAI_API_KEY"] = "",
                    ["AZURE_OPENAI_ENDPOINT"] = "",
                    ["AZURE_OPENAI_DEPLOYMENT"] = ""
                })));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health/detailed");
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var services = root.GetProperty("services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.3.4", root.GetProperty("version").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("One or more services degraded", root.GetProperty("message").GetString());
        Assert.Equal("not_configured", services.GetProperty("pinecone").GetProperty("status").GetString());
        Assert.Equal("not_configured", services.GetProperty("supabase").GetProperty("status").GetString());
        Assert.Equal("not_configured", services.GetProperty("azure_openai").GetProperty("status").GetString());
        Assert.Equal("not_configured", services.GetProperty("embedding_model").GetProperty("status").GetString());
    }
}

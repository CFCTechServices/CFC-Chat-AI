using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CFC.ChatAI.Api.Tests;

/// <summary>
/// Contract gate before IIS cutover (§4 of
/// local/csharp-api-scaffolding-and-health-swap.md):
/// the .NET <c>GET /api/health</c> must be byte-compatible with the
/// golden Python body, or the <c>DotNet_Health</c> rule stays disabled.
/// </summary>
public sealed class HealthContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    // Golden body captured from Python:
    //   GET /api/health → { ok:true, message:"CFC Animal Feed Software Chatbot API is running", version:"1.0.0" }
    // Source: app/api/endpoints/health.py :: health_check with app/config.py Settings.API_VERSION.
    private const string ExpectedMessage = "CFC Animal Feed Software Chatbot API is running";
    private const string ExpectedVersion = "1.0.0";

    public HealthContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_Returns200_WithJsonContentType()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetHealth_MatchesPythonGoldenBody()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Exact property names — snake_case policy must NOT rename these,
        // and must not silently camelCase future top_k-style fields.
        Assert.True(root.TryGetProperty("ok", out var ok), "missing 'ok'");
        Assert.True(root.TryGetProperty("message", out var message), "missing 'message'");
        Assert.True(root.TryGetProperty("version", out var version), "missing 'version'");

        Assert.Equal(JsonValueKind.True, ok.ValueKind);
        Assert.Equal(ExpectedMessage, message.GetString());
        Assert.Equal(ExpectedVersion, version.GetString());

        // No extra / missing members vs the Pydantic HealthResponse shape.
        Assert.Equal(3, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task GetHealth_SnakeCasePolicy_SurvivesGlobalOptionChange()
    {
        // Regression guard: if someone flips the global naming policy to
        // camelCase, this fails fast instead of drifting top_k/doc_id-style
        // fields on later endpoints. See
        // local/pydantic-models-and-csharp-migration.md §1–2.
        var response = await _client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"ok\"", body);
        Assert.Contains("\"message\"", body);
        Assert.Contains("\"version\"", body);
        Assert.DoesNotContain("\"Ok\"", body);
        Assert.DoesNotContain("\"Message\"", body);
        Assert.DoesNotContain("\"Version\"", body);
    }
}

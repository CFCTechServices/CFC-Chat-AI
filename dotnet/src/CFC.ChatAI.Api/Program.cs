using System.Text.Json;
using CFC.ChatAI.Api.Endpoints;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Services;
using CFC.ChatAI.Api.Authentication;
using CFC.ChatAI.Api.Infrastructure.Supabase;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddProjectDotEnv(builder.Environment.ContentRootPath,
    Environment.GetEnvironmentVariable("DOTENV_PATH"));

// Mirror app/config.py — version comes from config, not hardcoded per endpoint.
builder.Configuration.AddEnvironmentVariables();
var apiVersion = builder.Configuration["API_VERSION"] ?? builder.Configuration["ApiVersion"] ?? "1.0.0";
builder.Services.AddSingleton(new ApiInfo(
    builder.Configuration["ApiTitle"] ?? "CFC Animal Feed Software Chatbot API",
    apiVersion));

builder.Services.AddSingleton(sp => RuntimeOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<RuntimeDiagnostics>();
builder.Services.AddSupabaseInfrastructure();
builder.Services.AddSupabaseAuthentication();
builder.Services.AddSelfServiceRateLimits(builder.Configuration);
builder.Services.AddHttpClient(RuntimeDiagnostics.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
    .RedactLoggedHeaders(_ => true)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

// Preserve Pydantic wire format: snake_case names, nulls emitted, no camelCasing.
// (See local/pydantic-models-and-csharp-migration.md §1–2.)
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    // Dictionary keys may be external identifiers (e.g. Pinecone namespaces); never rename them.
    o.SerializerOptions.DictionaryKeyPolicy = null;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapAllEndpoints(); // one line per route group lives inside EndpointExtensions
app.Run();

public record ApiInfo(string Title, string Version);

// Exposes the implicitly-generated Program class to WebApplicationFactory<T>
// for contract tests (tests/CFC.ChatAI.Api.Tests).
public partial class Program;

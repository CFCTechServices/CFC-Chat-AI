using System.Text.Json;
using CFC.ChatAI.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Mirror app/config.py — version comes from config, not hardcoded per endpoint.
builder.Configuration.AddEnvironmentVariables();
var apiVersion = builder.Configuration["ApiVersion"] ?? "1.0.0";
builder.Services.AddSingleton(new ApiInfo(
    builder.Configuration["ApiTitle"] ?? "CFC Animal Feed Software Chatbot API",
    apiVersion));

// Preserve Pydantic wire format: snake_case names, nulls emitted, no camelCasing.
// (See local/pydantic-models-and-csharp-migration.md §1–2.)
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});

var app = builder.Build();
app.MapAllEndpoints(); // one line per route group lives inside EndpointExtensions
app.Run();

public record ApiInfo(string Title, string Version);

// Exposes the implicitly-generated Program class to WebApplicationFactory<T>
// for contract tests (tests/CFC.ChatAI.Api.Tests).
public partial class Program;

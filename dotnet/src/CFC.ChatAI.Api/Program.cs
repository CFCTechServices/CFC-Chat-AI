using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CFC.ChatAI.Api.Configuration;
using CFC.ChatAI.Api.Endpoints;
using CFC.ChatAI.Api.Features.Health;
using CFC.ChatAI.Api.Security;
using CFC.ChatAI.Pinecone;
using CFC.ChatAI.Feedback;
using CFC.ChatAI.ChatService;
using CFC.ChatAI.Supabase;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Conversations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;

// The server is launched from the repository root. Preserve explicitly set environment variables.
DotNetEnv.Env.NoClobber().Load(".env");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Api:Urls"] ?? "http://127.0.0.1:5000");

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// Keep health available without credentials. Provider-backed routes are never mapped without
// their complete configuration: no mock successes and no half-configured adapters.
var config = builder.Configuration;
var hasSupabase = new[] { "SUPABASE_URL", "SUPABASE_ANON_KEY", "SUPABASE_SERVICE_ROLE_KEY" }
    .All(key => !string.IsNullOrWhiteSpace(config[key]));
var hasPinecone = new[] { "PINECONE_API_KEY", "PINECONE_INDEX_NAME", "Embedding:ModelPath", "Embedding:VocabularyPath" }
    .All(key => !string.IsNullOrWhiteSpace(config[key]));
if (hasSupabase)
{
    builder.Services.AddSupabaseInfrastructure(o =>
    {
        o.Url = config["SUPABASE_URL"]!;
        o.AnonKey = config["SUPABASE_ANON_KEY"]!;
        o.ServiceRoleKey = config["SUPABASE_SERVICE_ROLE_KEY"]!;
        o.DocumentsBucket = config["SUPABASE_DOCUMENTS_BUCKET"] ?? o.DocumentsBucket;
        o.VideosBucket = config["SUPABASE_VIDEOS_BUCKET"] ?? o.VideosBucket;
    });
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<AdminSettingsStore>();
    builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, ApiAuthenticationHandler>("Bearer", null);
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("current-user", policy => policy.RequireAuthenticatedUser().RequireClaim("active", "true"))
        .AddPolicy("admin", policy => policy.RequireAuthenticatedUser().RequireClaim("active", "true")
            .RequireRole("admin", "superuser"));
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.AddPolicy("public-auth", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    });
    if (hasPinecone)
    {
        builder.Services.AddPineconeInfrastructure(o =>
        {
            o.ApiKey = config["PINECONE_API_KEY"]!;
            o.IndexName = config["PINECONE_INDEX_NAME"]!;
            o.ModelPath = config["Embedding:ModelPath"]!;
            o.VocabularyPath = config["Embedding:VocabularyPath"]!;
        });
        builder.Services.AddFeedback();
        builder.Services.AddChatService(o =>
        {
            o.AzureOpenAIEndpoint = config["AZURE_OPENAI_ENDPOINT"] ?? "";
            o.AzureOpenAIApiKey = config["AZURE_OPENAI_API_KEY"] ?? "";
            o.AzureOpenAIDeploymentName = config["AZURE_OPENAI_DEPLOYMENT"] ?? "";
        });
    }
}

var app = builder.Build();
// Do not leak dependency exceptions (which can contain provider data) to HTTP clients.
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (BadHttpRequestException)
    {
        if (!context.Response.HasStarted)
            await Results.BadRequest(new { detail = "Invalid request body" }).ExecuteAsync(context);
    }
    catch (SessionDeletionPartialFailureException)
    {
        app.Logger.LogError("Session deletion partially failed; reconciliation required (trace {TraceId})", context.TraceIdentifier);
        if (!context.Response.HasStarted)
            await Results.Problem("Messages were deleted but session deletion failed; retry or contact an operator.", statusCode: 500).ExecuteAsync(context);
    }
    catch (DependencyFailureException)
    {
        if (!context.Response.HasStarted)
            await Results.Problem("A dependency is unavailable.", statusCode: 503).ExecuteAsync(context);
    }
    catch (Exception)
    {
        app.Logger.LogError("Internal API request failed (trace {TraceId})", context.TraceIdentifier);
        if (!context.Response.HasStarted)
            await Results.Problem("Request could not be completed.", statusCode: 500).ExecuteAsync(context);
    }
});
app.MapHealth();
if (hasSupabase)
{
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapInternalApi(hasPinecone);
    app.MapAdminReads();
}
app.Run();

public partial class Program;

using System.Net.Http.Headers;
using CFC.ChatAI.Api.Configuration;
using Microsoft.Extensions.Options;

namespace CFC.ChatAI.Api.Features.Health;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/api/health", (IOptions<ApiOptions> options) =>
                new HealthResponse(
                    true,
                    "CFC Animal Feed Software Chatbot API is running",
                    options.Value.Version))
            .WithTags("health");

        app.MapGet("/api/health/detailed", async (
                IConfiguration configuration,
                IHttpClientFactory httpClientFactory,
                IOptions<ApiOptions> options,
                CancellationToken cancellationToken) =>
            {
                var checks = new Dictionary<string, object?>
                {
                    ["pinecone"] = await CheckPineconeAsync(configuration, httpClientFactory, cancellationToken),
                    ["supabase"] = await CheckSupabaseAsync(configuration, httpClientFactory, cancellationToken),
                    ["azure_openai"] = CheckAzureOpenAi(configuration),
                    ["embedding_model"] = CheckEmbeddingModel(configuration)
                };

                var allHealthy = checks.Values.All(check =>
                    check is IReadOnlyDictionary<string, object?> values &&
                    values.TryGetValue("status", out var status) &&
                    Equals(status, "ok"));

                return new DetailedHealthResponse(
                    allHealthy,
                    options.Value.Version,
                    allHealthy ? "All systems operational" : "One or more services degraded",
                    checks);
            })
            .WithTags("health");
    }

    private static async Task<IReadOnlyDictionary<string, object?>> CheckPineconeAsync(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["PINECONE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Status("not_configured", ("detail", "PINECONE_API_KEY not set"));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.pinecone.io/indexes");
            request.Headers.Add("Api-Key", apiKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await httpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
                return Status("ok", ("index", configuration["PINECONE_INDEX_NAME"] ?? "cfc-rag-chatbot"));

            return Status("error", ("detail", $"Pinecone returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                           !cancellationToken.IsCancellationRequested)
        {
            return Status("error", ("detail", exception.Message));
        }
    }

    private static async Task<IReadOnlyDictionary<string, object?>> CheckSupabaseAsync(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var url = configuration["SUPABASE_URL"];
        var serviceRoleKey = configuration["SUPABASE_SERVICE_ROLE_KEY"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceRoleKey))
            return Status("not_configured", ("detail", "SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY not set"));

        try
        {
            var endpoint = new Uri($"{url.TrimEnd('/')}/rest/v1/profiles?select=id&limit=1");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation("apikey", serviceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await httpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
                return Status("ok", ("url", url));

            return Status("error", ("detail", $"Supabase returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                           !cancellationToken.IsCancellationRequested)
        {
            return Status("error", ("detail", exception.Message));
        }
    }

    private static IReadOnlyDictionary<string, object?> CheckEmbeddingModel(IConfiguration configuration)
    {
        var modelPath = configuration["Embedding:ModelPath"] ?? configuration["Pinecone:ModelPath"];
        var vocabularyPath = configuration["Embedding:VocabularyPath"] ?? configuration["Pinecone:VocabularyPath"];
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(vocabularyPath))
            return Status("not_configured", ("detail", "Embedding model and vocabulary paths not configured"));

        if (!File.Exists(modelPath) || !File.Exists(vocabularyPath))
            return Status("error", ("detail", "Embedding model or vocabulary file not found"));

        return Status(
            "ok",
            ("model", configuration["Embedding:ModelName"] ?? "sentence-transformers/all-MiniLM-L6-v2"));
    }

    private static IReadOnlyDictionary<string, object?> CheckAzureOpenAi(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["AZURE_OPENAI_API_KEY"]) ||
            string.IsNullOrWhiteSpace(configuration["AZURE_OPENAI_ENDPOINT"]))
            return Status("not_configured", ("detail", "AZURE_OPENAI_API_KEY or AZURE_OPENAI_ENDPOINT not set"));

        var deployment = configuration["AZURE_OPENAI_DEPLOYMENT"];
        return string.IsNullOrWhiteSpace(deployment)
            ? Status("degraded", ("detail", "AZURE_OPENAI_DEPLOYMENT not set — LLM disabled"))
            : Status("ok", ("deployment", deployment));
    }

    private static IReadOnlyDictionary<string, object?> Status(
        string status,
        params (string Key, object? Value)[] values)
    {
        var result = new Dictionary<string, object?> { ["status"] = status };
        foreach (var (key, value) in values)
            result[key] = value;
        return result;
    }
}

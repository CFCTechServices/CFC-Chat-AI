using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.Pinecone;
using CFC.ChatAI.Pinecone.Internal;
using CFC.ChatAI.Supabase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pinecone;

[assembly: WebApplicationFactoryContentRoot("CFC.ChatAI.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", "/app", "CFC.ChatAI.Api.dll", "0")]

namespace CFC.ChatAI.UploadParity.Runner;

// This executable is deliberately usable only inside the network-less Linux container.
// It hosts the real API/handler/processor, captures Supabase HTTP after serialization,
// and substitutes only the internal embedding and vector-store boundaries.
internal static class UploadParityRunner
{
    private const string Bucket = "parity-bucket";
    private const string Provider = "https://provider.invalid";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Expected fixture path, filename, failure kind, output path.");
        try
        {
            EnsureNetworklessContainer();
            var socketDenied = await CheckOutboundDenied();
            if (!socketDenied) throw new InvalidOperationException("Outbound socket self-test failed.");
            var capture = new Capture(args[2]);
            var handler = new ProviderHandler(capture);
            var unknownDenied = await handler.SelfTest();
            if (!unknownDenied) throw new InvalidOperationException("Unhandled HTTP self-test failed.");
            using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseContentRoot("/app");
                builder.UseSetting("SUPABASE_URL", Provider);
                builder.UseSetting("SUPABASE_ANON_KEY", "test-anon");
                builder.UseSetting("SUPABASE_SERVICE_ROLE_KEY", "test-service");
                builder.UseSetting("SUPABASE_DOCUMENTS_BUCKET", Bucket);
                builder.UseSetting("SUPABASE_VIDEOS_BUCKET", Bucket);
                builder.UseSetting("PINECONE_API_KEY", "test-pinecone");
                builder.UseSetting("PINECONE_INDEX_NAME", "parity-index");
                builder.UseSetting("PINECONE_NAMESPACE", "parity-namespace");
                builder.UseSetting("Embedding:ModelPath", "unused.onnx");
                builder.UseSetting("Embedding:VocabularyPath", "unused.txt");
                builder.UseSetting("Api:EnableDevIngest", "true");
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("CFC.ChatAI.Supabase").ConfigurePrimaryHttpMessageHandler(() => handler);
                    services.RemoveAll<IEmbeddingModel>();
                    services.AddSingleton<IEmbeddingModel>(new TestEmbedding(capture));
                    services.RemoveAll<IVectorStore>();
                    services.AddSingleton<IVectorStore>(new RecordingVectorStore(capture));
                    services.AddSingleton<IStartupFilter>(new LoopbackFilter());
                });
            });
            using var client = factory.CreateClient();
            if (factory.Server.GetType() != typeof(TestServer))
                throw new InvalidOperationException("The upload must use in-process TestServer.");
            var pineconeOptions = factory.Services.GetRequiredService<PineconeOptions>();
            var supabaseOptions = factory.Services.GetRequiredService<SupabaseOptions>();
            if (pineconeOptions.Namespace != "parity-namespace" || supabaseOptions.DocumentsBucket != Bucket || supabaseOptions.Url != Provider)
                throw new InvalidOperationException("Test provider settings did not reach the application.");
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(args[0])), "file", args[1]);
            using var response = await client.PostAsync("/api/ingest/dev", form);
            var responseText = await response.Content.ReadAsStringAsync();
            object? responseBody;
            try { responseBody = JsonSerializer.Deserialize<JsonElement>(responseText); }
            catch (JsonException) { responseBody = responseText; }
            var result = new
            {
                status = (int)response.StatusCode,
                response = responseBody,
                operations = capture.Operations,
                embeddings = capture.Embeddings,
                capture_levels = new { supabase = "wire", pinecone = "application" },
                namespace_configured = pineconeOptions.Namespace,
                namespace_observed = "unobservable at the substituted IVectorStore boundary",
                self_test = new { socket_denied = socketDenied, unhandled_http_denied = unknownDenied, in_process_server = true },
                failure_requested = args[2],
                failure_intercepted = capture.FailureIntercepted,
                unexpected_requests = capture.UnexpectedRequests,
            };
            await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(result, Json));
            return capture.UnexpectedRequests == 0 && (args[2] == "none" || capture.FailureIntercepted) ? 0 : 2;
        }
        catch (Exception e)
        {
            // Dummy settings only; do not emit request headers or environment variables.
            await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(new { harness_error = e.GetType().Name, detail = e.Message }, Json));
            return 1;
        }
    }

    private static void EnsureNetworklessContainer()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/.dockerenv") ||
            Environment.GetEnvironmentVariable("UPLOAD_PARITY_ISOLATED") != "1")
            throw new InvalidOperationException("Run only in the prescribed Docker container with --network none.");
        var interfaces = Directory.GetFileSystemEntries("/sys/class/net").Select(Path.GetFileName).ToArray();
        if (interfaces.Length != 1 || interfaces[0] != "lo")
            throw new InvalidOperationException("Container has a non-loopback network interface.");
        if (File.Exists("/tmp/.env") || File.Exists("/app/.env"))
            throw new InvalidOperationException("Unexpected dotenv file in runner paths.");
    }

    private static async Task<bool> CheckOutboundDenied()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await socket.ConnectAsync(IPAddress.Parse("192.0.2.1"), 443, timeout.Token); return false; }
        catch (SocketException) { return true; }
        catch (OperationCanceledException) { return false; } // A timeout alone does not establish denial.
    }

    private sealed class LoopbackFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await continuation(); });
            next(app);
        };
    }

    private sealed class Capture(string failure)
    {
        public List<object> Operations { get; } = [];
        public List<object> Embeddings { get; } = [];
        public bool FailureIntercepted { get; private set; }
        public int UnexpectedRequests { get; set; }
        public bool ShouldFail(string kind)
        {
            if (!FailureIntercepted && failure == kind) { FailureIntercepted = true; return true; }
            return false;
        }
    }

    private sealed class ProviderHandler(Capture capture) : HttpMessageHandler
    {
        public async Task<bool> SelfTest()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Provider + "/unhandled-self-test");
                await Handle(request, recordUnexpected: false, CancellationToken.None);
                return false;
            }
            catch (InvalidOperationException) { return true; }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Handle(request, recordUnexpected: true, cancellationToken);

        private async Task<HttpResponseMessage> Handle(HttpRequestMessage request, bool recordUnexpected, CancellationToken token)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI.");
            var path = uri.AbsolutePath;
            var kind = uri.GetLeftPart(UriPartial.Authority) != Provider ? "unexpected" :
                request.Method == HttpMethod.Post && path.StartsWith("/storage/v1/object/" + Bucket + "/", StringComparison.Ordinal)
                    ? path.Contains("/original/", StringComparison.Ordinal) ? "storage-original"
                        : path.Contains("/sections/", StringComparison.Ordinal) ? "storage-section"
                        : path.Contains("/images/", StringComparison.Ordinal) ? "storage-image" : "unexpected"
                    : path == "/rest/v1/document_chunks" && request.Method == HttpMethod.Post ? "chunks-upsert"
                    : path == "/rest/v1/document_chunks" && request.Method == HttpMethod.Delete ? "chunks-delete" : "unexpected";
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(token);
            var headers = new Dictionary<string, string>();
            var allowedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "content-type", "content-length", "contenttype", "x-upsert", "prefer", "accept", "user-agent", "x-client-info" };
            foreach (var header in request.Headers)
                if (allowedHeaders.Contains(header.Key)) headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            if (request.Content is not null)
                foreach (var header in request.Content.Headers)
                    if (allowedHeaders.Contains(header.Key)) headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            object? body = null;
            if (bytes.Length > 0)
            {
                if (kind is "chunks-upsert" or "storage-section")
                {
                    try { body = JsonSerializer.Deserialize<JsonElement>(bytes); }
                    catch (JsonException) { body = new { invalid_json = true, size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }; }
                }
                else body = new { size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            }
            var fail = capture.ShouldFail(kind);
            if (recordUnexpected || kind != "unexpected")
                capture.Operations.Add(new { provider = "supabase", capture = "wire", kind, method = request.Method.Method,
                    origin = uri.GetLeftPart(UriPartial.Authority), path, query = uri.Query, headers, body,
                    provider_response_status = kind == "unexpected" ? (int?)null : fail ? 503 : 200,
                    result = fail ? "injected-failure" : kind == "unexpected" ? "blocked" : "canned-success" });
            if (kind == "unexpected")
            {
                if (recordUnexpected) capture.UnexpectedRequests++;
                throw new InvalidOperationException("Unhandled provider request was blocked.");
            }
            return new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(fail ? "{\"message\":\"injected failure\"}" : "{}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestEmbedding(Capture capture) : IEmbeddingModel
    {
        public float[] EncodeQuery(string query) => Encode([query])[0];
        public List<float[]> Encode(IReadOnlyList<string> texts)
        {
            var result = new List<float[]>();
            foreach (var text in texts)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                long sum = 0, weighted = 0;
                for (var i = 0; i < bytes.Length; i++) { sum += bytes[i]; weighted += (long)(i + 1) * bytes[i]; }
                float[] vector = [bytes.Length, sum % 997, weighted % 997, bytes.Count(b => b == 32)];
                capture.Embeddings.Add(new { input = text, values = vector });
                result.Add(vector);
            }
            return result;
        }
    }

    private sealed class RecordingVectorStore(Capture capture) : IVectorStore
    {
        public Task UpsertAsync(IEnumerable<VectorRecord> records)
        {
            var vectors = records.Select(record => new { id = record.Id, values = record.Values,
                metadata = record.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value?.Value) }).ToArray();
            var fail = capture.ShouldFail("vectors-upsert");
            capture.Operations.Add(new { provider = "pinecone", capture = "application", kind = "vectors-upsert", vectors,
                result = fail ? "injected-failure" : "canned-success" });
            if (fail) throw new InvalidOperationException("Injected vector failure.");
            return Task.CompletedTask;
        }
        public Task DeleteAsync(IReadOnlyCollection<string> ids)
        {
            capture.Operations.Add(new { provider = "pinecone", capture = "application", kind = "vectors-delete", ids, result = "canned-success" });
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] vector, int topK = 5, bool includeMetadata = true, Metadata? filter = null) =>
            throw new InvalidOperationException("Unexpected vector query blocked.");
        public Task<DescribeIndexStatsResponse> GetIndexStatsAsync() =>
            throw new InvalidOperationException("Unexpected index stats blocked.");
    }
}

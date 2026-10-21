using System.Net;
using System.Text;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Feedback;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Storage;
using CFC.ChatAI.Supabase.Internal;

namespace CFC.ChatAI.Supabase.Tests;

public sealed class SupabaseAdapterTests
{
    [Fact]
    public async Task ChunkUpsert_UsesTypedWireRowsAndServiceRoleHeaders()
    {
        var handler = new RecordingHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var transport = CreateTransport(handler);
        var repository = new SupabaseChunkRepository(transport);

        await repository.UpsertAsync([
            new Chunk("chunk-1", "doc-1", "content", SectionId: "section-1", ImagePaths: ["images/a.png"]),
        ]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/rest/v1/document_chunks?on_conflict=chunk_id", handler.PathAndQuery);
        Assert.Equal("Bearer service-secret", handler.Authorization);
        Assert.Equal("service-secret", handler.ApiKey);
        Assert.Contains("\"chunk_id\":\"chunk-1\"", handler.Body);
        Assert.Contains("\"doc_id\":\"doc-1\"", handler.Body);
        Assert.Contains("\"content\":\"content\"", handler.Body);
        Assert.DoesNotContain("embedding", handler.Body.ToLowerInvariant());
    }

    [Fact]
    public async Task IdentityGateway_MapsUnauthorizedToAbsentUserButPropagatesOutage()
    {
        var unauthorizedHandler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var identity = new SupabaseIdentityGateway(CreateTransport(unauthorizedHandler));
        Assert.Null(await identity.GetUserAsync("expired-token"));
        Assert.Equal("Bearer expired-token", unauthorizedHandler.Authorization);
        Assert.Equal("anon-secret", unauthorizedHandler.ApiKey);

        var failedHandler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("contains-private-provider-details"),
        }));
        var failedIdentity = new SupabaseIdentityGateway(CreateTransport(failedHandler));
        var exception = await Assert.ThrowsAsync<DependencyFailureException>(
            () => failedIdentity.GetUserAsync("valid-shaped-token"));
        Assert.DoesNotContain("contains-private-provider-details", exception.Message);
    }

    [Fact]
    public async Task FeedbackVote_UsesOneAtomicRpcCall()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var repository = new SupabaseFeedbackRepository(CreateTransport(handler));

        await repository.SubmitMessageFeedbackAsync(new FeedbackSubmission("message-1", "user-1", -1));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/rest/v1/rpc/submit_message_feedback", handler.PathAndQuery);
        Assert.Contains("\"p_message_id\":\"message-1\"", handler.Body);
        Assert.Contains("\"p_user_id\":\"user-1\"", handler.Body);
        Assert.Contains("\"p_new_rating\":-1", handler.Body);
    }

    [Fact]
    public async Task AdminSessionRevocation_UsesGlobalScopeEndpoint()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var gateway = new SupabaseAdminIdentityGateway(CreateTransport(handler));

        await gateway.RevokeSessionsAsync("user-1");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/auth/v1/admin/users/user-1/logout", handler.PathAndQuery);
        Assert.Contains("\"scope\":\"global\"", handler.Body);
        Assert.Equal("Bearer service-secret", handler.Authorization);
    }

    [Fact]
    public async Task StorageUpload_DoesNotTakeOwnershipOfCallerStream()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var storage = new SupabaseObjectStorage(CreateTransport(handler), CreateOptions());
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("payload"));

        await storage.UploadAsync(
            new ObjectKey(StorageArea.Documents, "docs/doc-1/file.txt"),
            input,
            new UploadOptions("text/plain", Overwrite: true));

        Assert.True(input.CanRead);
        Assert.Equal("/storage/v1/object/cfc-docs/docs/doc-1/file.txt", handler.PathAndQuery);
        Assert.Equal("true", handler.Upsert);
    }

    private static SupabaseTransport CreateTransport(RecordingHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler)), CreateOptions());

    private static SupabaseOptions CreateOptions() => new()
    {
        Url = "https://project.supabase.co",
        AnonKey = "anon-secret",
        ServiceRoleKey = "service-secret",
        DocumentsBucket = "cfc-docs",
        VideosBucket = "cfc-videos",
    };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal HttpMethod? Method { get; private set; }
        internal string? PathAndQuery { get; private set; }
        internal string? Authorization { get; private set; }
        internal string? ApiKey { get; private set; }
        internal string? Upsert { get; private set; }
        internal string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            ApiKey = request.Headers.GetValues("apikey").Single();
            Upsert = request.Headers.TryGetValues("x-upsert", out var values) ? values.Single() : null;
            if (request.Content is not null && request.Method != HttpMethod.Get)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return await send(request, cancellationToken);
        }
    }
}

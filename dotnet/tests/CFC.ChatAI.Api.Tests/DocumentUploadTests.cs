using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Identity;
using CFC.ChatAI.Supabase.Profiles;
using CFC.ChatAI.Supabase.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CFC.ChatAI.Api.Tests;

public sealed class DocumentUploadTests
{
    private static WebApplicationFactory<Program> Factory(string role = "admin", bool dev = false, string environment = "Development", string ip = "127.0.0.1", bool devDelete = false, string status = "active", bool deleted = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("SUPABASE_URL", "https://example.supabase.co");
            builder.UseSetting("SUPABASE_ANON_KEY", "anon");
            builder.UseSetting("SUPABASE_SERVICE_ROLE_KEY", "secret");
            builder.UseSetting("PINECONE_API_KEY", "secret");
            builder.UseSetting("PINECONE_INDEX_NAME", "test");
            builder.UseSetting("Embedding:ModelPath", "test.onnx");
            builder.UseSetting("Embedding:VocabularyPath", "vocab.txt");
            builder.UseSetting("Api:EnableDevIngest", dev.ToString());
            builder.UseSetting("Api:EnableDevDelete", devDelete.ToString());
            builder.UseSetting("Document:MaxUploadBytes", "8");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdentityGateway>(); services.AddSingleton<IIdentityGateway>(new Identity());
                services.RemoveAll<IUserProfileRepository>(); services.AddSingleton<IUserProfileRepository>(new Profiles(role, status, deleted));
                services.RemoveAll<IChunkRepository>(); services.AddSingleton<IChunkRepository, Chunks>();
                services.RemoveAll<IDocumentHandler>(); services.AddSingleton<IDocumentHandler, Handler>();
                services.AddSingleton<IStartupFilter>(new RemoteIpFilter(IPAddress.Parse(ip)));
            });
        });

    private static MultipartFormDataContent Form(string name = "test.txt", string text = "hello", string field = "file")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), field, name);
        return form;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? token = "valid")
    {
        var client = factory.CreateClient();
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task Admin_success_returns_dto(string role)
    {
        using var factory = Factory(role); using var client = Client(factory);
        using var response = await client.PostAsync("/api/files/upload", Form("Test Name.txt"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test-name", body.GetProperty("document_id").GetString());
        Assert.Equal(1, body.GetProperty("chunk_count").GetInt32());
        Assert.False(body.TryGetProperty("source_object", out _));
        Assert.True(((Handler)factory.Services.GetRequiredService<IDocumentHandler>()).ReceivedCancellationToken);
    }

    [Fact]
    public async Task Authorization_and_request_validation()
    {
        using var factory = Factory(); using var client = Client(factory);
        using var anonymous = Client(factory, null); using var invalid = Client(factory, "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/files/upload", Form())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.PostAsync("/api/files/upload", Form())).StatusCode);
        using var userFactory = Factory("user"); using var user = Client(userFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/files/upload", Form())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", new StringContent("invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", new MultipartFormDataContent())).StatusCode);
        using var many = Form(); many.Add(new StringContent("x"), "file", "other.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", many)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", Form(field: "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", Form("../bad.txt"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", Form("CON.txt"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", new StringContent("broken", Encoding.UTF8, "multipart/form-data"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/files/upload", Form("movie.mp4"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", Form(text: ""))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/files/upload", Form(text: "123456789"))).StatusCode);
        using var unknownLength = new MultipartFormDataContent();
        unknownLength.Add(new UnknownLengthContent(), "file", "large.txt");
        Assert.Null(unknownLength.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/files/upload", unknownLength)).StatusCode);
    }

    [Theory]
    [InlineData("Café Guide V2.EML", "café-guide-v2")]
    [InlineData("Cafe\u0301 Guide V2.EML", "café-guide-v2")]
    [InlineData("日本語ガイド.EML", "日本語ガイド")]
    [InlineData("!!!.EML", "document")]
    public async Task Unicode_filename_preflight_uses_same_id_as_ingest_and_duplicate_lookup(string name, string id)
    {
        using var factory = Factory(); using var client = Client(factory);
        var chunks = (Chunks)factory.Services.GetRequiredService<IChunkRepository>();
        using var response = await client.PostAsync("/api/files/upload", Form(name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("document_id").GetString());
        Assert.Equal(name, body.GetProperty("source_name").GetString());
        Assert.Equal(id, chunks.LastLookup);
        chunks.Existing = id;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/files/upload", Form(name.ToUpperInvariant().Normalize(NormalizationForm.FormD)))).StatusCode);
        Assert.Equal(1, ((Handler)factory.Services.GetRequiredService<IDocumentHandler>()).IngestCalls);
    }

    [Fact]
    public async Task Browser_style_utf8_filename_without_filename_star_is_accepted()
    {
        using var factory = Factory(); using var client = Client(factory);
        using var form = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "--test\r\nContent-Disposition: form-data; name=\"file\"; filename=\"Café Guide V2.EML\"\r\n\r\nhello\r\n--test--\r\n"));
        form.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=test");
        using var response = await client.PostAsync("/api/files/upload", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("café-guide-v2", body.GetProperty("document_id").GetString());
    }

    [Fact]
    public async Task Unsafe_or_overlong_filenames_do_not_reach_providers()
    {
        using var factory = Factory(); using var client = Client(factory);
        foreach (var name in new[] { "../café.eml", "a\\b.eml", "a%2fb.eml", "a%255Cb.eml", "a\u0001b.eml", "CON.EML", new string('é', 129) + ".eml" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/files/upload", Form(name))).StatusCode);
        Assert.Null(((Chunks)factory.Services.GetRequiredService<IChunkRepository>()).LastLookup);
        Assert.Equal(0, ((Handler)factory.Services.GetRequiredService<IDocumentHandler>()).IngestCalls);
    }

    [Theory]
    [InlineData("café-guide-v2", "docs/!u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml")]
    [InlineData("cafe\u0301-guide-v2", "docs/cafe\u0301-guide-v2/original/cafe\u0301-guide-v2.eml")]
    public async Task Download_and_delete_use_the_exact_decoded_id(string id, string path)
    {
        var key = new ObjectKey(StorageArea.Documents, path);
        var storage = new DownloadStorage(key);
        using var factory = Factory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IObjectStorage>(); services.AddSingleton<IObjectStorage>(storage);
        }));
        using var client = Client(factory);
        var chunks = (Chunks)factory.Services.GetRequiredService<IChunkRepository>();
        chunks.Existing = id;
        var route = "/api/admin/documents/" + Uri.EscapeDataString(id);

        using var response = await client.GetAsync(route + "/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, chunks.LastLookup);
        Assert.Equal(key, storage.SignedKey);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(id, ((Handler)factory.Services.GetRequiredService<IDocumentHandler>()).DeletedId);
    }

    [Fact]
    public async Task Conflict_and_dev_gates()
    {
        using var factory = Factory(dev: true); using var client = Client(factory);
        var chunks = (Chunks)factory.Services.GetRequiredService<IChunkRepository>();
        chunks.Existing = "test-name";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/files/upload", Form("Test Name.pdf"))).StatusCode);
        using var anonymous = Client(factory, null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsync("/api/ingest/dev", Form("Test Name.txt"))).StatusCode);
        using var offFactory = Factory(); using var off = Client(offFactory, null);
        Assert.Equal(HttpStatusCode.NotFound, (await off.PostAsync("/api/ingest/dev", Form())).StatusCode);
        using var prodFactory = Factory(dev: true, environment: "Production"); using var prod = Client(prodFactory, null);
        Assert.Equal(HttpStatusCode.NotFound, (await prod.PostAsync("/api/ingest/dev", Form())).StatusCode);
        using var ipv6Factory = Factory(dev: true, ip: "::1"); using var ipv6 = Client(ipv6Factory, null);
        Assert.Equal(HttpStatusCode.OK, (await ipv6.PostAsync("/api/ingest/dev", Form())).StatusCode);
        using var remoteFactory = Factory(dev: true, ip: "192.0.2.1"); using var remote = Client(remoteFactory, null);
        remote.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.PostAsync("/api/ingest/dev", Form())).StatusCode);
    }

    [Fact]
    public async Task Missing_pinecone_configuration_leaves_uploads_unmapped()
    {
        using var factory = Factory(dev: true).WithWebHostBuilder(builder => builder.UseSetting("PINECONE_API_KEY", ""));
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/files/upload", Form())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/ingest/dev", Form())).StatusCode);
    }

    [Fact]
    public async Task Cancellation_reaches_the_handler()
    {
        using var factory = Factory(); using var client = Client(factory);
        var handler = (Handler)factory.Services.GetRequiredService<IDocumentHandler>();
        handler.WaitForCancellation = true;
        using var cancel = new CancellationTokenSource();
        var request = client.PostAsync("/api/files/upload", Form(), cancel.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Errors_are_sanitized_and_never_success()
    {
        using var factory = Factory(); using var client = Client(factory);
        var handler = (Handler)factory.Services.GetRequiredService<IDocumentHandler>();
        handler.Error = new DocumentProcessingException("secret");
        using var unreadable = await client.PostAsync("/api/files/upload", Form());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unreadable.StatusCode);
        handler.Error = new DependencyFailureException("secret", "operation");
        using var unavailable = await client.PostAsync("/api/files/upload", Form());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("secret", await unavailable.Content.ReadAsStringAsync());
        handler.Error = new AggregateException("secret");
        using var failed = await client.PostAsync("/api/files/upload", Form());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("secret", await failed.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task Delete_admin_and_dev_share_handler(string role)
    {
        using var factory = Factory(role, devDelete: true); using var client = Client(factory);
        var handler = (Handler)factory.Services.GetRequiredService<IDocumentHandler>();
        var response = await client.DeleteAsync("/api/admin/documents/sample");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Document deleted.", json.GetProperty("message").GetString());
        Assert.Equal("sample", handler.DeletedId);
        Assert.True(handler.ReceivedCancellationToken);
        using var anonymous = Client(factory, null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.DeleteAsync("/api/ingest/dev/other")).StatusCode);
        Assert.Equal("other", handler.DeletedId);
    }

    [Fact]
    public async Task Delete_auth_validation_and_errors()
    {
        using var factory = Factory(); using var client = Client(factory);
        using var anonymous = Client(factory, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        using var userFactory = Factory(role: "user"); using var user = Client(userFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        using var inactiveFactory = Factory(status: "inactive"); using var inactive = Client(inactiveFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await inactive.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        using var deletedFactory = Factory(deleted: true); using var deleted = Client(deletedFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await deleted.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        var handler = (Handler)factory.Services.GetRequiredService<IDocumentHandler>();
        foreach (var id in new[] { "a%2Fb", "a%252fb", "a%5Cb", "caf%25C3%25A9", "a%01b" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync("/api/admin/documents/" + id)).StatusCode);
        Assert.Null(handler.DeletedId);
        handler.Found = false;
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        handler.Error = new UnsupportedDeletionException();
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        handler.Error = new DependencyFailureException("secret", "operation");
        var unavailable = await client.DeleteAsync("/api/admin/documents/sample");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("secret", await unavailable.Content.ReadAsStringAsync());
        handler.Error = new Exception("secret");
        var failure = await client.DeleteAsync("/api/admin/documents/sample");
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("secret", await failure.Content.ReadAsStringAsync());
        Assert.Contains("Reference:", await failure.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_dev_requires_independent_flag_development_and_direct_loopback()
    {
        using var offFactory = Factory(dev: true); using var off = Client(offFactory, null);
        Assert.Equal(HttpStatusCode.NotFound, (await off.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
        using var prodFactory = Factory(devDelete: true, environment: "Production"); using var prod = Client(prodFactory, null);
        Assert.Equal(HttpStatusCode.NotFound, (await prod.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
        using var ipv6Factory = Factory(devDelete: true, ip: "::1"); using var ipv6 = Client(ipv6Factory, null);
        Assert.Equal(HttpStatusCode.OK, (await ipv6.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
        using var remoteFactory = Factory(devDelete: true, ip: "192.0.2.1"); using var remote = Client(remoteFactory, null);
        remote.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
        using var missingFactory = Factory(devDelete: true).WithWebHostBuilder(b => b.UseSetting("PINECONE_API_KEY", ""));
        using var missing = Client(missingFactory);
        Assert.Equal(HttpStatusCode.NotFound, (await missing.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await missing.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
        using var noSupabaseFactory = Factory(devDelete: true).WithWebHostBuilder(b => b.UseSetting("SUPABASE_SERVICE_ROLE_KEY", ""));
        using var noSupabase = Client(noSupabaseFactory);
        Assert.Equal(HttpStatusCode.NotFound, (await noSupabase.DeleteAsync("/api/admin/documents/sample")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await noSupabase.DeleteAsync("/api/ingest/dev/sample")).StatusCode);
    }

    [Fact]
    public async Task Delete_cancellation_reaches_handler()
    {
        using var factory = Factory(); using var client = Client(factory);
        var handler = (Handler)factory.Services.GetRequiredService<IDocumentHandler>();
        handler.WaitForCancellation = true;
        using var cancel = new CancellationTokenSource();
        var request = client.DeleteAsync("/api/admin/documents/sample", cancel.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync("123456789"u8.ToArray()).AsTask();
    }

    private sealed class RemoteIpFilter(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) => { context.Connection.RemoteIpAddress = ip; await nextMiddleware(); });
            next(app);
        };
    }
    private sealed class Identity : IIdentityGateway
    {
        public Task<IdentityUser?> GetUserAsync(string token, CancellationToken ct = default) => Task.FromResult(token == "valid" ? new IdentityUser("cf52745a-430b-4fef-91d1-9287cbbf2c33", "test@example.com") : null);
        public Task RequestPasswordRecoveryAsync(string email, Uri? redirectTo = null, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class Profiles(string role, string status, bool deleted) : IUserProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(string id, string token, CancellationToken ct = default) => Task.FromResult<Profile?>(new Profile(id, "test@example.com", "Test", null, role, status, DateTimeOffset.UtcNow, deleted ? DateTimeOffset.UtcNow : null, null));
        public Task<Profile?> UpdateCurrentAsync(string id, string token, UpdateOwnProfile update, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Handler : IDocumentHandler
    {
        public Exception? Error { get; set; }
        public bool ReceivedCancellationToken { get; private set; }
        public bool WaitForCancellation { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int IngestCalls { get; private set; }
        public async Task<DocumentHandlerResult> IngestAsync(DocumentHandlerInput input, CancellationToken ct = default)
        {
            IngestCalls++;
            ReceivedCancellationToken = ct.CanBeCanceled;
            if (Error != null) throw Error;
            if (WaitForCancellation)
            {
                Started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            }
            await input.Content.CopyToAsync(Stream.Null, ct);
            return new(DocumentProcessor.DocumentProcessor.CreateDocumentId(input.FileName), input.FileName, "document", 1, 1, new ObjectKey(StorageArea.Documents, "test"));
        }
        public bool Found { get; set; } = true;
        public string? DeletedId { get; private set; }
        public async Task<DocumentDeletionResult> DeleteAsync(string id, CancellationToken ct = default)
        {
            DeletedId = id;
            ReceivedCancellationToken = ct.CanBeCanceled;
            if (Error != null) throw Error;
            if (WaitForCancellation)
            {
                Started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            }
            return new(id, Found, 1, 1);
        }
    }
    private sealed class DownloadStorage(ObjectKey key) : IObjectStorage
    {
        public ObjectKey? SignedKey { get; private set; }
        public Task<Page<StorageEntry>> ListAsync(ObjectPrefix prefix, PageRequest page, CancellationToken ct = default) =>
            Task.FromResult(new Page<StorageEntry>(key.Area == prefix.Area &&
                Path.GetDirectoryName(key.Path)!.Replace('\\', '/') == prefix.Path ? [new StorageEntry(key, false)] : [], false));
        public Task<Uri> CreateSignedUrlAsync(ObjectKey objectKey, TimeSpan lifetime, CancellationToken ct = default)
        {
            SignedKey = objectKey;
            return Task.FromResult(new Uri("https://example.invalid/signed"));
        }
        public Task<StoredObject> UploadAsync(ObjectKey key, Stream content, UploadOptions options, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DownloadedObject> DownloadAsync(ObjectKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(IReadOnlyList<ObjectKey> keys, CancellationToken ct = default) => throw new NotSupportedException();
        public Uri GetPublicUrl(ObjectKey key) => throw new NotSupportedException();
    }
    private sealed class Chunks : IChunkRepository
    {
        public string? Existing { get; set; }
        public string? LastLookup { get; private set; }
        public Task<DocumentMetadata?> GetDocumentMetadataAsync(string id, CancellationToken ct = default)
        {
            LastLookup = id;
            return Task.FromResult(id == Existing ? new DocumentMetadata(id, "test", "document") : null);
        }
        public Task UpsertAsync(IReadOnlyList<Chunk> chunks, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Chunk>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChunkIdPage> ListIdsForDocumentAsync(string id, PageRequest page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<DocumentSummary>> ListDocumentSummariesAsync(PageRequest page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

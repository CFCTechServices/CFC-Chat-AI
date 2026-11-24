using System.Net;
using System.Net.Http.Json;
using System.Text;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Services.Admin;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    private sealed class UploadFile(string name, long? size = null) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => size ?? 7;
        public string ContentType => "application/octet-stream";
        public int Opens { get; private set; }
        public bool FailFirstOpen { get; init; }
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opens++;
            if (FailFirstOpen && Opens == 1) throw new IOException("Fixture open failure.");
            Assert.InRange(Size, 1, maxAllowedSize);
            return new MemoryStream("fixture"u8.ToArray());
        }
    }

    private HttpResponseMessage UploadConfig(string? backendName = "dotnet", string? documentUploads = "true") =>
        Ok(backend.Config with { Backend = backendName, DocumentUploads = documentUploads });

    private static HttpResponseMessage IngestedFile() => Ok(new
    {
        document_id = Guide.Id, source_name = "guide.docx", source_type = "document", chunk_count = 4, vector_count = 4
    });

    [Fact]
    public async Task DisabledUploadTabAllowsLocalSelectionAndLeavesOtherAdminTabsUsable()
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/config" => UploadConfig(documentUploads: "false"),
            "/api/admin/users" => AdminList(Target),
            "/api/admin/documents" => DocumentList(Guide),
            _ => throw new InvalidOperationException("Unexpected network request")
        });
        using var services = HistoryServices(http, new TestNavigation("admin"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            await page.QuiescenceTask;
            await renderer.ClickButtonAsync<Admin>("Upload");
            var html = page.ToHtmlString();
            Assert.Contains("Submission is disabled", html);
            foreach (var area in Enum.GetValues<UploadArea>()) Assert.Contains($"id=\"upload-{area}\"", html);
            Assert.Contains("multiple", html);
            Assert.Contains("Start bulk upload", html);
            Assert.DoesNotContain(".mp4", html);
            await renderer.ClickButtonAsync<Admin>("Content");
            Assert.Contains("4 chunks indexed", page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Users");
            Assert.Contains(Target.Email, page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData("user", "user-a")]
    [InlineData("dev", "user-a")]
    [InlineData("admin", "other-account")]
    public async Task UploadRequiresCurrentAdminBeforeAnyRequest(string role, string owner)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unexpected network request"));
        using var work = new AdminUploadWorkspace(new(http), session, owner);
        var file = new UploadFile("sample.docx");
        await work.CheckAvailabilityAsync();
        work.Select(UploadArea.Single, [file]);
        await work.SubmitAsync(UploadArea.Single);
        Assert.False(work.Available);
        Assert.Equal(0, file.Opens);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("python", "true")]
    [InlineData("dotnet", null)]
    [InlineData("dotnet", "false")]
    [InlineData("unavailable", null)]
    public async Task UploadRechecksDotNetCapabilityBeforeOpeningFile(string? backendName, string? capability)
    {
        await RestoreRole("superuser");
        var probes = 0;
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/api/auth/config", req.RequestUri!.AbsolutePath);
            Assert.Null(req.Headers.Authorization);
            if (++probes == 1) return UploadConfig();
            return backendName == "unavailable" ? new(HttpStatusCode.ServiceUnavailable) : UploadConfig(backendName, capability);
        });
        using var work = new AdminUploadWorkspace(new(http), session, "user-a");
        var file = new UploadFile("sample.docx");
        await work.CheckAvailabilityAsync();
        Assert.True(work.Available);
        work.Select(UploadArea.Single, [file]);
        await work.SubmitAsync(UploadArea.Single);
        Assert.False(work.Available);
        Assert.Equal(UploadState.Blocked, Assert.Single(work.Items(UploadArea.Single)).Result.State);
        Assert.Equal(2, probes);
        Assert.Equal(0, file.Opens);
    }

    [Theory]
    [InlineData(UploadArea.Single, "guide.DOCX", 20, true)]
    [InlineData(UploadArea.Bulk, "film.M4V", 1, false)]
    [InlineData(UploadArea.Single, "film.mov", 10, false)]
    [InlineData(UploadArea.Bulk, "film.MKV", 10, false)]
    [InlineData(UploadArea.EmailPdf, "mail.PDF", 10, true)]
    [InlineData(UploadArea.Email, "mail.eml", 10, true)]
    [InlineData(UploadArea.Bulk, "mail.pdf", 10, true)]
    [InlineData(UploadArea.EmailPdf, "mail.eml", 10, false)]
    [InlineData(UploadArea.Email, "mail.msg", 10, false)]
    [InlineData(UploadArea.Single, "notes.txt", 10, true)]
    [InlineData(UploadArea.Bulk, "notes.md", 10, true)]
    [InlineData(UploadArea.Single, "../file.doc", 10, false)]
    [InlineData(UploadArea.Single, "CONIN$.doc", 10, false)]
    [InlineData(UploadArea.Single, "a%2fb.docx", 10, false)]
    [InlineData(UploadArea.Single, "a\"b.doc", 10, false)]
    [InlineData(UploadArea.Single, "empty.doc", 0, false)]
    [InlineData(UploadArea.Single, "large.doc", 21, false)]
    public async Task UploadValidatesPickerFormatFilenameAndSizeWithoutReadingFile(UploadArea area, string name, int size, bool valid)
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(_ => UploadConfig());
        using var work = new AdminUploadWorkspace(new(http, new(20)), session, "user-a");
        var file = new UploadFile(name, size);
        await work.CheckAvailabilityAsync();
        work.Select(area, [file]);
        Assert.Equal(valid ? UploadState.Ready : UploadState.Invalid, Assert.Single(work.Items(area)).Result.State);
        Assert.Equal(valid, work.CanStart(area));
        Assert.Equal(0, file.Opens);
    }

    [Theory]
    [InlineData(UploadArea.Single, "Guía.docx")]
    [InlineData(UploadArea.Bulk, "guide.DOC")]
    [InlineData(UploadArea.Email, "message.eml")]
    [InlineData(UploadArea.EmailPdf, "message.pdf")]
    public async Task UploadSendsOneBearerMultipartToDotNetAndRefreshesContent(UploadArea area, string name)
    {
        await RestoreRole("superuser");
        var posts = 0;
        var reads = 0;
        using var http = new HttpClient(new HistoryHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/config")
            {
                Assert.Null(req.Headers.Authorization);
                return UploadConfig();
            }
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal(browser.Session, req.Headers.Authorization?.Parameter);
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
            {
                reads++;
                Assert.Equal(HttpMethod.Get, req.Method);
                return DocumentList(Guide);
            }
            posts++;
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/api/files/upload", req.RequestUri.AbsolutePath);
            var form = Assert.IsType<MultipartFormDataContent>(req.Content);
            var part = Assert.Single(form);
            Assert.Equal("file", part.Headers.ContentDisposition!.Name!.Trim('"'));
            var wire = Encoding.UTF8.GetString(await form.ReadAsByteArrayAsync());
            Assert.Contains($"name=\"file\"; filename=\"{name}\"", wire);
            Assert.Contains("fixture", wire);
            return IngestedFile();
        })) { BaseAddress = new Uri("http://127.0.0.1:5000/") };
        using var work = new AdminUploadWorkspace(new(http), session, "user-a");
        await work.CheckAvailabilityAsync();
        work.Select(area, [new UploadFile(name)]);
        await work.SubmitAsync(area);
        var item = Assert.Single(work.Items(area));
        Assert.Equal(UploadState.Ingested, item.Result.State);
        Assert.Equal(Guide.Id, item.Result.DocumentId);
        Assert.Equal("Content library refreshed.", work.Notice);
        await work.SubmitAsync(area);
        await work.RetryAsync(area, item);
        Assert.Equal((1, 1), (posts, reads));
    }

    [Theory]
    [InlineData("denied", UploadState.Blocked)]
    [InlineData("expired", UploadState.Blocked)]
    [InlineData("server-error", UploadState.Unconfirmed)]
    [InlineData("oversize", UploadState.Failed)]
    [InlineData("rejected", UploadState.Failed)]
    [InlineData("duplicate", UploadState.Failed)]
    [InlineData("legacy-receipt", UploadState.Unconfirmed)]
    [InlineData("missing-id", UploadState.Unconfirmed)]
    [InlineData("zero-vectors", UploadState.Warning)]
    [InlineData("missing-chunks", UploadState.Unconfirmed)]
    [InlineData("missing-index", UploadState.Warning)]
    public async Task UploadDoesNotMistakeHttpOrPartialResultsForSuccess(string scenario, UploadState expected)
    {
        await RestoreRole("admin");
        var submits = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/config") return UploadConfig();
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
                return scenario == "missing-index" ? DocumentList() : DocumentList(Guide);
            submits++;
            return scenario switch
            {
                "denied" => new(HttpStatusCode.Forbidden),
                "expired" => new(HttpStatusCode.Unauthorized),
                "server-error" => new(HttpStatusCode.InternalServerError),
                "oversize" => new(HttpStatusCode.RequestEntityTooLarge),
                "rejected" => new(HttpStatusCode.UnsupportedMediaType),
                "duplicate" => new(HttpStatusCode.Conflict),
                "legacy-receipt" => Ok(new { ingestion = new { success = true, doc_id = Guide.Id, chunks_processed = 4 } }),
                "missing-id" => Ok(new { document_id = "", chunk_count = 4, vector_count = 4 }),
                "zero-vectors" => Ok(new { document_id = Guide.Id, chunk_count = 4, vector_count = 0 }),
                "missing-chunks" => Ok(new { document_id = Guide.Id, vector_count = 4 }),
                "missing-index" => IngestedFile(),
                _ => throw new InvalidOperationException()
            };
        });
        using var work = new AdminUploadWorkspace(new(http), session, "user-a");
        await work.CheckAvailabilityAsync();
        var first = new UploadFile("example.doc");
        var next = new UploadFile("second.doc");
        var authFailure = expected == UploadState.Blocked;
        work.Select(UploadArea.Bulk, authFailure ? [first, next] : [first]);
        await work.SubmitAsync(UploadArea.Bulk);
        var item = work.Items(UploadArea.Bulk)[0];
        Assert.Equal(expected, item.Result.State);
        if (authFailure)
        {
            Assert.False(work.Available);
            Assert.Equal(0, next.Opens);
        }
        await work.RetryAsync(UploadArea.Bulk, item);
        await work.SubmitAsync(UploadArea.Bulk);
        Assert.Equal(1, submits);
    }

    [Fact]
    public async Task BulkUploadSerializesFilesAndRetriesOnlyTheFileThatCouldNotOpen()
    {
        await RestoreRole("admin");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();
        var refreshes = 0;
        using var http = new HttpClient(new HistoryHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/config") return Task.FromResult(UploadConfig());
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
            {
                refreshes++;
                return Task.FromResult(refreshes == 1 ? new(HttpStatusCode.ServiceUnavailable) : DocumentList(Guide));
            }
            Assert.Equal("/api/files/upload", req.RequestUri.AbsolutePath);
            var part = Assert.Single(Assert.IsType<MultipartFormDataContent>(req.Content));
            writes.Add(part.Headers.ContentDisposition!.FileName!.Trim('"'));
            if (writes.Count > 1) return Task.FromResult(IngestedFile());
            started.SetResult();
            return firstResponse.Task;
        })) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var work = new AdminUploadWorkspace(new(http), session, "user-a");
        await work.CheckAvailabilityAsync();
        var first = new UploadFile("one.docx");
        var second = new UploadFile("two.pdf") { FailFirstOpen = true };
        var third = new UploadFile("three.md");
        work.Select(UploadArea.Bulk, [first, new UploadFile("oops.mp4")]);
        Assert.False(work.CanStart(UploadArea.Bulk));
        work.Select(UploadArea.Bulk, [first, second, third]);
        var batch = work.SubmitAsync(UploadArea.Bulk);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(work.Busy);
        Assert.Equal(0, second.Opens);
        await work.SubmitAsync(UploadArea.Bulk);
        work.Select(UploadArea.Bulk, []);
        firstResponse.SetResult(IngestedFile());
        await batch;
        Assert.Equal(new[] { first.Name, third.Name }, writes);
        Assert.Equal(1, refreshes);
        Assert.Contains("could not be refreshed", work.Notice);
        var retry = work.Items(UploadArea.Bulk)[1];
        Assert.Equal(UploadState.Retryable, retry.Result.State);
        await work.RetryAsync(UploadArea.Bulk, retry);
        Assert.Equal(UploadState.Ingested, retry.Result.State);
        Assert.Equal(new[] { first.Name, third.Name, second.Name }, writes);
        Assert.Equal(2, refreshes);
        Assert.Equal((1, 2, 1), (first.Opens, second.Opens, third.Opens));
    }
}

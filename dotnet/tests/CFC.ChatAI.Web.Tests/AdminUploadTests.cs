using System.Net;
using System.Net.Http.Json;
using System.Text;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    private static readonly AdminUploadPolicy ApprovedUploads = new("https://fixture.invalid/");

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

    private static HttpResponseMessage UploadJson(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage UploadSchema(string variant = "secured")
    {
        var bearer = new Dictionary<string, string[]> { ["BearerAuth"] = [] };
        var routes = new Dictionary<string, object>();
        foreach (var path in new[] { "/api/files/upload", "/api/files/bulk", "/api/videos/upload" })
        {
            if (variant == "missing-bulk" && path == "/api/files/bulk") continue;
            object post = new { security = new[] { bearer } };
            if (path == "/api/videos/upload")
            {
                if (variant == "anonymous") post = new { };
                if (variant == "optional") post = new { security = new object[] { bearer, new { } } };
            }
            routes.Add(path, new { post });
        }
        var schemes = new Dictionary<string, object>
        {
            ["BearerAuth"] = new { type = "http", scheme = variant == "basic" ? "basic" : "bearer" }
        };
        return UploadJson(new { paths = routes, components = new { securitySchemes = schemes } });
    }

    private static HttpResponseMessage IngestedFile() => UploadJson(new
    {
        message = "File uploaded and ingestion triggered",
        ingestion = new { success = true, chunks_processed = 4, doc_id = Guide.Id }, supabase = new { stored = true }
    });

    [Fact]
    public async Task UploadTabAllowsLocalSelectionAndLeavesOtherAdminTabsUsable()
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req => req.RequestUri!.AbsolutePath switch
        {
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
            await renderer.ClickButtonAsync<Admin>("Content");
            Assert.Contains("4 chunks indexed", page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Users");
            Assert.Contains(Target.Email, page.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(null, "admin", "user-a")]
    [InlineData("https://different.invalid/", "superuser", "user-a")]
    [InlineData("http://fixture.invalid/", "admin", "user-a")]
    [InlineData("https://fixture.invalid/", "user", "user-a")]
    [InlineData("https://fixture.invalid/", "dev", "user-a")]
    [InlineData("https://fixture.invalid/", "admin", "other-account")]
    public async Task UploadRequiresApprovedTargetAndCurrentAdminBeforeAnyRequest(string? approved, string role, string owner)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unexpected network request"));
        using var work = new AdminUploadWorkspace(new(http, new(approved)), session, owner);
        var file = new UploadFile("sample.docx");
        await work.CheckAvailabilityAsync();
        work.Select(UploadArea.Single, [file]);
        await work.SubmitAsync(UploadArea.Single);
        Assert.False(work.Available);
        Assert.Equal(0, file.Opens);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("optional")]
    [InlineData("missing-bulk")]
    [InlineData("basic")]
    [InlineData("unavailable")]
    public async Task UploadRechecksAllDeployedRoutesBeforeOpeningFile(string variant)
    {
        await RestoreRole("superuser");
        var probes = 0;
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/openapi.json", req.RequestUri!.AbsolutePath);
            Assert.Null(req.Headers.Authorization);
            if (++probes == 1) return UploadSchema();
            return variant == "unavailable" ? new(HttpStatusCode.ServiceUnavailable) : UploadSchema(variant);
        });
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads), session, "user-a");
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
    [InlineData(UploadArea.Bulk, "film.M4V", 1, true)]
    [InlineData(UploadArea.Single, "film.mov", 10, true)]
    [InlineData(UploadArea.Bulk, "film.MKV", 10, true)]
    [InlineData(UploadArea.EmailPdf, "mail.PDF", 10, true)]
    [InlineData(UploadArea.Email, "mail.eml", 10, true)]
    [InlineData(UploadArea.Bulk, "mail.pdf", 10, false)]
    [InlineData(UploadArea.EmailPdf, "mail.eml", 10, false)]
    [InlineData(UploadArea.Email, "mail.msg", 10, false)]
    [InlineData(UploadArea.Single, "notes.txt", 10, false)]
    [InlineData(UploadArea.Bulk, "notes.md", 10, false)]
    [InlineData(UploadArea.Single, "../file.doc", 10, false)]
    [InlineData(UploadArea.Single, " .. .mp4", 10, false)]
    [InlineData(UploadArea.Single, "a\"b.doc", 10, false)]
    [InlineData(UploadArea.Single, "empty.doc", 0, false)]
    [InlineData(UploadArea.Single, "large.doc", 21, false)]
    public async Task UploadValidatesPickerFormatFilenameAndSizeWithoutReadingFile(UploadArea area, string name, int size, bool valid)
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(_ => UploadSchema());
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads with { MaxFileBytes = 20 }), session, "user-a");
        var file = new UploadFile(name, size);
        await work.CheckAvailabilityAsync();
        work.Select(area, [file]);
        Assert.Equal(valid ? UploadState.Ready : UploadState.Invalid, Assert.Single(work.Items(area)).Result.State);
        Assert.Equal(valid, work.CanStart(area));
        Assert.Equal(0, file.Opens);
    }

    [Theory]
    [InlineData(UploadArea.Single, "guide.docx", false)]
    [InlineData(UploadArea.Bulk, "guide.DOC", false)]
    [InlineData(UploadArea.Email, "message.eml", false)]
    [InlineData(UploadArea.EmailPdf, "message.pdf", false)]
    [InlineData(UploadArea.Single, "Training  Clip.MP4", true)]
    [InlineData(UploadArea.Bulk, "Training  Clip.WebM", true)]
    public async Task UploadSendsPythonMultipartAndRefreshesAuthorizedContent(UploadArea area, string name, bool video)
    {
        await RestoreRole("superuser");
        var posts = 0;
        var reads = 0;
        using var http = new HttpClient(new HistoryHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath == "/openapi.json")
            {
                Assert.Null(req.Headers.Authorization);
                return UploadSchema();
            }
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal(browser.Session, req.Headers.Authorization?.Parameter);
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
            {
                reads++;
                Assert.Equal(HttpMethod.Get, req.Method);
                return DocumentList(Guide with { Id = video ? "training-clip" : Guide.Id });
            }
            posts++;
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal(video ? "/api/videos/upload" : "/api/files/upload", req.RequestUri.AbsolutePath);
            var form = Assert.IsType<MultipartFormDataContent>(req.Content);
            var parts = form.ToDictionary(p => p.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal(video ? 3 : 1, parts.Count);
            var file = parts["file"];
            Assert.Equal(name, file.Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal("fixture", Encoding.UTF8.GetString(await file.ReadAsByteArrayAsync()));
            if (!video) return IngestedFile();
            Assert.Equal("training-clip", await parts["slug"].ReadAsStringAsync());
            Assert.Equal("small", await parts["model"].ReadAsStringAsync());
            return UploadJson(new { ok = true, slug = "training-clip", transcripts = new { txt = "text-output" } });
        })) { BaseAddress = new Uri(ApprovedUploads.VerifiedBackendUrl!) };
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads), session, "user-a");
        await work.CheckAvailabilityAsync();
        work.Select(area, [new UploadFile(name)]);
        await work.SubmitAsync(area);
        var item = Assert.Single(work.Items(area));
        Assert.Equal(video ? UploadState.Transcribed : UploadState.Ingested, item.Result.State);
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
    [InlineData("bad-ingestion", UploadState.Failed)]
    [InlineData("missing-ingestion", UploadState.Unconfirmed)]
    [InlineData("missing-id", UploadState.Unconfirmed)]
    [InlineData("zero-chunks", UploadState.Warning)]
    [InlineData("missing-chunks", UploadState.Warning)]
    [InlineData("storage-error", UploadState.Warning)]
    [InlineData("missing-index", UploadState.Warning)]
    [InlineData("video-incomplete", UploadState.Unconfirmed)]
    public async Task UploadDoesNotMistakeHttpOrPartialResultsForSuccess(string scenario, UploadState expected)
    {
        await RestoreRole("admin");
        var submits = 0;
        using var http = HistoryHttp(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/openapi.json") return UploadSchema();
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
                return scenario == "missing-index" ? DocumentList() : DocumentList(Guide);
            submits++;
            return scenario switch
            {
                "denied" => new(HttpStatusCode.Forbidden),
                "expired" => new(HttpStatusCode.Unauthorized),
                "server-error" => new(HttpStatusCode.InternalServerError),
                "oversize" => new(HttpStatusCode.RequestEntityTooLarge),
                "rejected" => new(HttpStatusCode.UnprocessableEntity),
                "bad-ingestion" => UploadJson(new { message = "uploaded", ingestion = new { success = false, error = "internal detail" } }),
                "missing-ingestion" => UploadJson(new { message = "uploaded" }),
                "missing-id" => UploadJson(new { ingestion = new { success = true } }),
                "zero-chunks" => UploadJson(new { ingestion = new { success = true, doc_id = Guide.Id, chunks_processed = 0 } }),
                "missing-chunks" => UploadJson(new { ingestion = new { success = true, doc_id = Guide.Id }, supabase = new { stored = true } }),
                "storage-error" => UploadJson(new { ingestion = new { success = true, doc_id = Guide.Id, chunks_processed = 3 },
                    supabase = new { stored = false, error = "internal detail" } }),
                "missing-index" => IngestedFile(),
                "video-incomplete" => UploadJson(new { ok = true, slug = "example", transcripts = new { } }),
                _ => throw new InvalidOperationException()
            };
        });
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads), session, "user-a");
        await work.CheckAvailabilityAsync();
        var first = new UploadFile(scenario == "video-incomplete" ? "example.mp4" : "example.doc");
        var next = new UploadFile("second.doc");
        bool authFailure = expected == UploadState.Blocked;
        work.Select(UploadArea.Bulk, authFailure ? [first, next] : [first]);
        await work.SubmitAsync(UploadArea.Bulk);
        var item = work.Items(UploadArea.Bulk)[0];
        Assert.Equal(expected, item.Result.State);
        Assert.DoesNotContain("internal detail", item.Result.Detail);
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
    public async Task MixedBulkRoutesEachFileAndPreservesReceiptsWhenContentRefreshFails()
    {
        await RestoreRole("admin");
        var requests = new List<string>();
        using var http = HistoryHttp(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/openapi.json") return UploadSchema();
            requests.Add(path);
            return path switch
            {
                "/api/files/upload" => IngestedFile(),
                "/api/videos/upload" => UploadJson(new { ok = true, slug = "clip", transcripts = new { txt = "transcript" } }),
                "/api/admin/documents" => new(HttpStatusCode.ServiceUnavailable),
                _ => throw new InvalidOperationException("Unexpected upload route")
            };
        });
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads), session, "user-a");
        await work.CheckAvailabilityAsync();
        work.Select(UploadArea.Bulk, [new UploadFile("guide.docx"), new UploadFile("clip.mp4")]);
        await work.SubmitAsync(UploadArea.Bulk);
        Assert.Equal(new[] { "/api/files/upload", "/api/videos/upload", "/api/admin/documents" }, requests);
        Assert.Equal(new[] { UploadState.Ingested, UploadState.Transcribed }, work.Items(UploadArea.Bulk).Select(i => i.Result.State));
        Assert.Contains("could not be refreshed", work.Notice);
        Assert.False(work.CanStart(UploadArea.Bulk));
    }

    [Fact]
    public async Task BulkUploadSerializesSelectionAndRetriesOnlyFileThatCouldNotOpen()
    {
        await RestoreRole("admin");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();
        int refreshes = 0;
        using var http = new HttpClient(new HistoryHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/openapi.json") return Task.FromResult(UploadSchema());
            if (req.RequestUri.AbsolutePath == "/api/admin/documents")
            {
                refreshes++;
                return Task.FromResult(DocumentList(Guide));
            }
            var part = Assert.Single(Assert.IsType<MultipartFormDataContent>(req.Content));
            writes.Add(part.Headers.ContentDisposition!.FileName!.Trim('"'));
            if (writes.Count > 1) return Task.FromResult(IngestedFile());
            started.SetResult();
            return firstResponse.Task;
        })) { BaseAddress = new Uri(ApprovedUploads.VerifiedBackendUrl!) };
        using var work = new AdminUploadWorkspace(new(http, ApprovedUploads), session, "user-a");
        await work.CheckAvailabilityAsync();
        var first = new UploadFile("one.docx");
        var second = new UploadFile("two.docx") { FailFirstOpen = true };
        var third = new UploadFile("three.docx");
        work.Select(UploadArea.Bulk, [first, new UploadFile("oops.pdf")]);
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
        var retry = work.Items(UploadArea.Bulk)[1];
        Assert.Equal(UploadState.Retryable, retry.Result.State);
        await work.RetryAsync(UploadArea.Bulk, retry);
        Assert.Equal(UploadState.Ingested, retry.Result.State);
        Assert.Equal(new[] { first.Name, third.Name, second.Name }, writes);
        Assert.Equal(2, refreshes);
        Assert.Equal((1, 2, 1), (first.Opens, second.Opens, third.Opens));
    }
}

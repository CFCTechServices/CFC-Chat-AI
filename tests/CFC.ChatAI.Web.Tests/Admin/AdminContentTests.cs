using System.Net;
using System.Net.Http.Json;
using CFC.ChatAI.Web.Components;
using CFC.ChatAI.Web.Components.Pages;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Admin;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CFC.ChatAI.Web.Tests;

public sealed partial class SupabaseSessionTests
{
    private static readonly AdminDocument Guide = new("doc-123", @"folder\Guide.docx", "document", 4);
    private static HttpResponseMessage DocumentList(params AdminDocument[] rows) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new AdminDocumentList(rows, rows.Length)) };

    [Theory]
    [InlineData("admin")]
    [InlineData("superuser")]
    public async Task ContentTabDownloadsAndConfirmsDeletionOfNamedDocument(string role)
    {
        await RestoreRole(role);
        var deletes = 0;
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(browser.Session, req.Headers.Authorization?.Parameter);
            return req.RequestUri!.AbsolutePath switch
            {
                "/api/admin/users" => AdminList(Target),
                "/api/admin/documents" => deletes == 0 ? DocumentList(Guide) : DocumentList(),
                "/api/admin/documents/doc-123/download" => new(HttpStatusCode.OK) { Content = JsonContent.Create(
                    new DocumentDownload("https://store.invalid/original", "Guide.docx")) },
                "/api/admin/documents/doc-123" when req.Method == HttpMethod.Delete && ++deletes == 1 =>
                    new(HttpStatusCode.OK) { Content = JsonContent.Create(new DocumentDeletion(true, "Deleted")) },
                _ => throw new InvalidOperationException("Unexpected request")
            };
        });
        using var services = HistoryServices(http, new TestNavigation("admin"), browser);
        await using var renderer = new LoginRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = renderer.BeginRenderingComponent(typeof(SessionRouteView), RouteParameters(typeof(Admin)));
            await page.QuiescenceTask;
            Assert.Contains(Target.Email, page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Content");
            Assert.Contains("4 chunks indexed", page.ToHtmlString());
            await renderer.InputAsync("missing");
            Assert.Contains("No documents match", page.ToHtmlString());
            await renderer.InputAsync("DOC-123");
            await renderer.ClickButtonAsync<AdminContent>("Download");
            Assert.Contains("href=\"https://store.invalid/original\"", page.ToHtmlString());
            Assert.Contains("rel=\"noopener noreferrer\"", page.ToHtmlString());
            await renderer.ClickButtonAsync<AdminContent>("Delete");
            Assert.Contains("Guide.docx</strong> (doc-123)", page.ToHtmlString());
            await renderer.ClickButtonAsync<AdminContent>("Cancel");
            Assert.DoesNotContain("Confirm deletion", page.ToHtmlString());
            Assert.Equal(0, deletes);
            await renderer.ClickButtonAsync<AdminContent>("Delete");
            await renderer.ClickButtonAsync<AdminContent>("Confirm deletion");
            Assert.Equal(1, deletes);
            Assert.Contains("Removed Guide.docx", page.ToHtmlString());
            Assert.Contains("No documents in the knowledge base", page.ToHtmlString());
            await renderer.ClickButtonAsync<Admin>("Users");
            Assert.Contains(Target.Email, page.ToHtmlString());
        });
    }

    [Fact]
    public async Task ContentSearchFiltersByBasenameOrIdAndResetsTwentyRowPagination()
    {
        await RestoreRole("admin");
        var rows = Enumerable.Range(1, 22).Select(i => new AdminDocument($"item-{i}", $"folder/manual-{i}.doc", "document", i)).Append(Guide).ToArray();
        using var http = HistoryHttp(_ => DocumentList(rows));
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        Assert.Equal(20, work.Visible.Count());
        work.MovePage(1);
        Assert.Equal(3, work.Visible.Count());
        work.Search(" gUiDe ");
        Assert.Equal(1, work.Page);
        Assert.Equal(Guide, Assert.Single(work.Visible));
        work.Search("DOC-123");
        Assert.Equal(Guide, Assert.Single(work.Visible));
        work.Search("folder");
        Assert.Empty(work.Visible);
        work.Search(null);
        Assert.Equal(23, work.MatchCount);
    }

    [Theory]
    [InlineData("user", "user-a")]
    [InlineData("dev", "user-a")]
    [InlineData("admin", "other")]
    public async Task ContentRejectsUnauthorizedRoleOrOwnerBeforeTransport(string role, string owner)
    {
        await RestoreRole(role);
        using var http = HistoryHttp(_ => throw new InvalidOperationException("Unauthorized request."));
        using var work = new AdminContentWorkspace(new(http), session, owner);
        await work.LoadAsync();
        work.Begin(Guide, DocumentAction.Delete);
        Assert.Null(work.Pending);
        Assert.False(work.Loaded);
        Assert.NotNull(work.Error);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://storage.invalid/file")]
    [InlineData("https://user@storage.invalid/file")]
    [InlineData("/relative")]
    public async Task UnusableDownloadLinkIsNeverOffered(string url)
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req => req.RequestUri!.AbsolutePath.EndsWith("/download")
            ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new DocumentDownload(url, "manual.doc")) } : DocumentList(Guide));
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        await work.PrepareDownloadAsync(Guide);
        Assert.Null(work.Download);
        Assert.Contains("unusable", work.Error);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task MissingOriginalOrRevokedDownloadDoesNotPublishALink(int status)
    {
        await RestoreRole("superuser");
        using var http = HistoryHttp(req => req.RequestUri!.AbsolutePath.EndsWith("/download")
            ? new((HttpStatusCode)status) { Content = JsonContent.Create(new { detail = "private storage detail" }) } : DocumentList(Guide));
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        await work.PrepareDownloadAsync(Guide);
        Assert.Null(work.Download);
        Assert.DoesNotContain("private storage detail", work.Error);
        Assert.Equal(status == 404, work.Loaded);
    }

    private sealed class WordFile(string name, long size = 4) : IBrowserFile
    {
        public string Name => name;
        public long Size => size;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public string ContentType => "application/octet-stream";
        public bool Opened { get; private set; }
        public Stream OpenReadStream(long maxAllowedSize, CancellationToken cancellationToken)
        {
            Assert.Equal(AdminUploadPolicy.DefaultMaxBytes, maxAllowedSize);
            Opened = true;
            return new MemoryStream("test"u8.ToArray());
        }
    }

    [Theory]
    [InlineData("wrong.pdf", 4)]
    [InlineData("../wrong.docx", 4)]
    [InlineData(@"..\wrong.doc", 4)]
    [InlineData("empty.docx", 0)]
    [InlineData("huge.docx", AdminUploadPolicy.DefaultMaxBytes + 1)]
    public async Task InvalidReplacementNeverOpensFile(string filename, long size)
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return DocumentList(Guide);
        });
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.Begin(Guide, DocumentAction.Replace);
        var file = new WordFile(filename, size);
        await work.ConfirmAsync(work.Pending!, file);
        Assert.NotNull(work.Error);
        Assert.False(file.Opened);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("ingestion failed")]
    [InlineData("put failed")]
    [InlineData("index unavailable")]
    [InlineData("missing result")]
    [InlineData("wrong document ID")]
    public async Task ReplacementRequiresIngestionAndFreshIndexAndNeverReplays(string outcome)
    {
        await RestoreRole("admin");
        var writes = 0;
        var replacement = Guide with { ChunkCount = 2 };
        using var http = HistoryHttp(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                if (writes == 0) return DocumentList(Guide);
                if (outcome == "index unavailable") return new(HttpStatusCode.ServiceUnavailable);
                return outcome == "missing result" ? DocumentList() : DocumentList(replacement);
            }
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("/api/admin/documents/doc-123/replace", req.RequestUri!.AbsolutePath);
            writes++;
            return outcome == "put failed" ? new(HttpStatusCode.BadRequest) : new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new DocumentReplacement(true, "Accepted",
                    new(outcome != "ingestion failed", outcome == "wrong document ID" ? "different-id" : replacement.Id)))
            };
        });
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.Begin(Guide, DocumentAction.Replace);
        var change = work.Pending!;
        var file = new WordFile(Guide.Name);
        await work.ConfirmAsync(change, file);
        await work.ConfirmAsync(change, file);
        Assert.True(file.Opened);
        Assert.Equal(1, writes);
        if (outcome == "ok")
        {
            Assert.Equal(replacement, Assert.Single(work.Visible));
            Assert.Contains("Indexed replacement", work.Notice);
        }
        else { Assert.Null(work.Notice); Assert.NotNull(work.Error); }
    }

    [Fact]
    public async Task AccountSwitchDuringDownloadDiscardsSignedLinkAndLibrary()
    {
        await RestoreRole("admin");
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new HistoryHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/download")
            ? held.Task : Task.FromResult(DocumentList(Guide)))) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        var preparing = work.PrepareDownloadAsync(Guide);
        var second = ActiveProfile() with { Id = "second", Role = "admin" };
        browser.Session = TestJwt(new { sub = second.Id, exp = clock.Now.AddHours(1).ToUnixTimeSeconds() });
        backend.Profile = _ => Task.FromResult(second);
        await session.BrowserSessionChanged("SIGNED_IN", second.Id);
        held.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new DocumentDownload("https://store.invalid/original", "Guide.docx")) });
        await preparing;
        Assert.Null(work.Download);
        Assert.False(work.Loaded);
        Assert.Empty(work.Visible);
        Assert.NotNull(work.Error);
    }

    [Fact]
    public async Task DeleteAcknowledgmentWithDocumentStillIndexedDoesNotReportSuccess()
    {
        await RestoreRole("admin");
        using var http = HistoryHttp(req => req.Method == HttpMethod.Delete
            ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new DocumentDeletion(true, "Deleted")) } : DocumentList(Guide));
        using var work = new AdminContentWorkspace(new(http), session, "user-a");
        await work.LoadAsync();
        work.Begin(Guide, DocumentAction.Delete);
        await work.ConfirmAsync(work.Pending!);
        Assert.Equal(Guide, Assert.Single(work.Visible));
        Assert.Null(work.Notice);
        Assert.Contains("did not confirm", work.Error);
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFC.ChatAI.Web.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services;

public enum DocumentAction { Replace, Delete }
public sealed record DocumentChange(AdminDocument Document, DocumentAction Action);

public sealed class AdminContentWorkspace(FastApiClient api, SupabaseSessionService session, string ownerId) : IDisposable
{
    private const int PageSize = 20;
    public const long MaxReplacementBytes = 50 * 1024 * 1024;
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<AdminDocument> documents = [];
    private IReadOnlyList<AdminDocument> matches = [];
    public int Count => documents.Count;
    public int MatchCount => matches.Count;
    public int Page { get; private set; } = 1;
    public int PageCount => Math.Max(1, (MatchCount + PageSize - 1) / PageSize);
    public IEnumerable<AdminDocument> Visible => matches.Skip((Page - 1) * PageSize).Take(PageSize);
    public string Query { get; private set; } = "";
    public bool Loaded { get; private set; }
    public bool Busy { get; private set; }
    public string? Notice { get; private set; }
    public string? Error { get; private set; }
    public DocumentChange? Pending { get; private set; }
    public DocumentDownload? Download { get; private set; }
    public string? DownloadId { get; private set; }

    public void Search(string? query)
    {
        if (Busy || Pending is not null) return;
        Query = query ?? "";
        Filter();
        Download = null;
    }

    private void Filter()
    {
        Page = 1;
        var term = Query.Trim();
        matches = term.Length == 0 ? documents : documents.Where(doc =>
            doc.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || doc.Id.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public void MovePage(int delta)
    {
        if (Busy || Pending is not null) return;
        Page = Math.Clamp(Page + delta, 1, PageCount);
        Download = null;
    }

    public Task LoadAsync() => RunAsync(async () =>
    {
        Pending = null;
        await ReloadAsync();
    }, "Could not load the content library. Refresh to try again.");

    private async Task ReloadAsync()
    {
        SetDocuments([], false);
        var result = await RequestAsync(api.GetDocumentsAsync);
        SetDocuments(result.Documents, true);
    }

    private void SetDocuments(IReadOnlyList<AdminDocument> rows, bool loaded)
    {
        documents = rows;
        Loaded = loaded;
        Filter();
    }

    private bool CanSelect(AdminDocument doc) => !Busy && Loaded && Pending is null && documents.Contains(doc) &&
        !lifetime.IsCancellationRequested && session.HasAdminAccess(ownerId);

    public Task PrepareDownloadAsync(AdminDocument doc)
    {
        if (!CanSelect(doc)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var result = await RequestAsync((token, ct) => api.GetDocumentDownloadAsync(token, doc.Id, ct));
            if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var url) || url.UserInfo.Length != 0 ||
                result.Url.Any(char.IsControl) || result.Url.Contains('\\') ||
                !(url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback)))
            {
                Error = "FastAPI returned an unusable download link.";
                return;
            }
            DownloadId = doc.Id;
            Download = result with { Url = url.AbsoluteUri };
        }, "Could not obtain a download link. Try again.", missing: "The original file is unavailable in storage.");
    }

    public void Begin(AdminDocument doc, DocumentAction action)
    {
        if (!CanSelect(doc)) return;
        Notice = Error = null;
        Download = null;
        Pending = new(doc, action);
    }

    public void Cancel() { if (!Busy) Pending = null; }

    public Task ConfirmAsync(DocumentChange change, IBrowserFile? file = null)
    {
        if (Busy || !ReferenceEquals(change, Pending) || lifetime.IsCancellationRequested) return Task.CompletedTask;
        if (change.Action == DocumentAction.Replace &&
            (file is null || !FastApiClient.IsWordFilename(file.Name) || file.Size is <= 0 or > MaxReplacementBytes))
        {
            Error = "Choose a nonempty .doc or .docx file of 50 MiB or less.";
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            var doc = change.Document;
            var latest = await RequestAsync(api.GetDocumentsAsync);
            if (!latest.Documents.Contains(doc))
            {
                Pending = null;
                SetDocuments(latest.Documents, true);
                Error = "The selected document changed. Review the refreshed list.";
                return;
            }
            if (file is not null && HasFilenameConflict(latest.Documents, doc, file.Name))
            {
                Error = "That filename belongs to another indexed document. Choose a different replacement.";
                return;
            }
            try
            {
                string? indexedId = null;
                bool confirmed;
                if (change.Action == DocumentAction.Delete)
                {
                    confirmed = (await RequestAsync((token, ct) => api.DeleteDocumentAsync(token, doc.Id, ct))).Success;
                }
                else
                {
                    var result = await RequestAsync(async (token, ct) =>
                    {
                        await using var stream = file!.OpenReadStream(MaxReplacementBytes, ct);
                        return await api.ReplaceDocumentAsync(token, doc.Id, file.Name, stream, ct);
                    });
                    confirmed = result.Success && result.Ingestion.Success;
                    indexedId = result.Ingestion.Id;
                }
                Pending = null;
                Notice = confirmed ? "FastAPI accepted the change." : null;
                await ReloadAsync();
                var originalStillIndexed = documents.Any(d => d.Id == doc.Id);
                var replacement = change.Action == DocumentAction.Replace ? documents.FirstOrDefault(d =>
                    indexedId is null ? d.Name.Equals(file!.Name, StringComparison.OrdinalIgnoreCase) : d.Id == indexedId) : null;
                var indexMatches = change.Action == DocumentAction.Delete ? !originalStillIndexed :
                    replacement is not null && (replacement.Id == doc.Id || !originalStillIndexed);
                if (!confirmed || !indexMatches)
                {
                    Notice = null;
                    Error = "The refreshed index did not confirm the requested change. Review the library before retrying.";
                }
                else Notice = change.Action == DocumentAction.Delete ? $"Removed {doc.Name} from the index." : $"Indexed replacement {file!.Name}.";
            }
            catch
            {
                // A timeout or failed ingestion can occur after the original was purged.
                Pending = null;
                SetDocuments([], false);
                throw;
            }
        }, "The change could not be confirmed. The original may already have been removed. Refresh before retrying.");
    }

    private static bool HasFilenameConflict(IReadOnlyList<AdminDocument> rows, AdminDocument target, string filename)
    {
        // Python indexes the replacement by a slug of the uploaded filename.
        var slug = Regex.Replace(Path.GetFileNameWithoutExtension(filename).Trim().ToLowerInvariant(), @"[^\p{L}\p{N}_]+", "-").Trim('-');
        return rows.Any(row => row.Id != target.Id && (row.Id == (slug.Length == 0 ? "document" : slug) ||
            row.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)));
    }

    private Task<T> RequestAsync<T>(Func<string, CancellationToken, Task<T>> request)
    {
        if (lifetime.IsCancellationRequested) throw new OperationCanceledException();
        return session.WithAdminTokenAsync(ownerId, request, lifetime.Token);
    }

    private async Task RunAsync(Func<Task> action, string failure, string? missing = null)
    {
        if (Busy || lifetime.IsCancellationRequested) return;
        Busy = true;
        Notice = Error = null;
        Download = null;
        DownloadId = null;
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or OperationCanceledException or JSException)
        {
            if (!session.HasAdminAccess(ownerId) || ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            {
                SetDocuments([], false);
                Pending = null;
                Error = "Administrator access could not be verified. Refresh or sign in again.";
            }
            else if (Notice is not null) Error = "FastAPI accepted the change, but the index could not be refreshed.";
            else if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } && missing is not null)
                Error = missing;
            else Error = failure;
            Notice = null;
        }
        finally { Busy = false; }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

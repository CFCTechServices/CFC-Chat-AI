using System.Net;
using System.Text.Json;
using CFC.ChatAI.Web.Services.Api;
using CFC.ChatAI.Web.Services.Auth;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CFC.ChatAI.Web.Services.Admin;

public enum UploadState { Ready, Invalid, Sending, Ingested, Warning, Failed, Unconfirmed, Retryable, Blocked }
public sealed record UploadResult(UploadState State, string Detail, string? DocumentId = null);
public sealed class UploadEntry(IBrowserFile file)
{
    internal IBrowserFile File { get; } = file;
    public string Name => File.Name;
    public long Size => File.Size;
    public int Progress { get; internal set; }
    public UploadResult Result { get; internal set; } = new(UploadState.Ready, "Ready");
}

public sealed class AdminUploadWorkspace(ChatApiClient api, SupabaseSessionService session, string owner) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<UploadArea, List<UploadEntry>> queues = Enum.GetValues<UploadArea>().ToDictionary(area => area, _ => new List<UploadEntry>());
    public bool Available { get; private set; }
    public bool Busy { get; private set; }
    public string? Notice { get; private set; }
    public long MaxBytes => api.MaxUploadBytes;
    public event Action? Changed;

    public IReadOnlyList<UploadEntry> Items(UploadArea area) => queues[area];
    public bool CanStart(UploadArea area) => Available && !Busy && session.HasAdminAccess(owner) &&
        !queues[area].Any(i => i.Result.State == UploadState.Invalid) && queues[area].Any(i => i.Result.State == UploadState.Ready);

    public async Task CheckAvailabilityAsync()
    {
        if (Busy || lifetime.IsCancellationRequested) return;
        Busy = true;
        Available = false;
        try
        {
            Available = session.HasAdminAccess(owner) && await api.CanSubmitUploadsAsync(lifetime.Token) &&
                !lifetime.IsCancellationRequested && session.HasAdminAccess(owner);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { Busy = false; }
    }

    public void Select(UploadArea area, IReadOnlyList<IBrowserFile> files)
    {
        if (Busy || lifetime.IsCancellationRequested || !session.HasAdminAccess(owner)) return;
        Notice = null;
        var queue = queues[area];
        queue.Clear();
        foreach (var file in files)
        {
            var entry = new UploadEntry(file);
            var error = area != UploadArea.Bulk && files.Count > 1 ? "Select one file here." :
                AdminUploadPolicy.Validate(area, file.Name, file.Size, MaxBytes);
            if (error is not null) entry.Result = new(UploadState.Invalid, error);
            queue.Add(entry);
        }
    }

    public Task SubmitAsync(UploadArea area) => CanStart(area)
        ? RunAsync(area, queues[area].Where(i => i.Result.State == UploadState.Ready).ToArray()) : Task.CompletedTask;

    public Task RetryAsync(UploadArea area, UploadEntry item) =>
        Available && !Busy && session.HasAdminAccess(owner) && queues[area].Contains(item) && item.Result.State == UploadState.Retryable
            ? RunAsync(area, [item]) : Task.CompletedTask;

    private async Task RunAsync(UploadArea area, IReadOnlyList<UploadEntry> batch)
    {
        var ct = lifetime.Token;
        Busy = true;
        Notice = null;
        try
        {
            foreach (var entry in batch)
            {
                ct.ThrowIfCancellationRequested();
                entry.Progress = 0;
                entry.Result = new(UploadState.Sending, "Transferring and processing…");
                Changed?.Invoke();
                var progress = new Progress<int>(value =>
                {
                    if (entry.Result.State != UploadState.Sending || ct.IsCancellationRequested) return;
                    entry.Progress = value;
                    Changed?.Invoke();
                });
                try
                {
                    entry.Result = await UploadAsync(entry.File, progress, ct);
                    if (entry.Result.DocumentId is not null) entry.Progress = 100;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    entry.Result = new(UploadState.Blocked, "Administrator authorization was denied. Check access and Content before resubmitting.");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    entry.Result = new(UploadState.Failed, "The deployment rejected the file size. Select a smaller file.");
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.UnprocessableEntity)
                {
                    entry.Result = new(UploadState.Failed, "The API rejected the file or its format. Check the selection before resubmitting.");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    entry.Result = new(UploadState.Failed, "A document with this ID already exists or is being processed. Check Content before resubmitting.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException or IOException or OperationCanceledException or JSException)
                {
                    entry.Result = new(UploadState.Unconfirmed, "The upload result could not be confirmed. Check Content before resubmitting.");
                }
                Changed?.Invoke();
                if (entry.Result.State == UploadState.Blocked || !session.HasAdminAccess(owner))
                {
                    Available = false;
                    break;
                }
            }
            var processed = batch.Where(i => i.Result.DocumentId is not null).ToList();
            if (processed.Count > 0 && session.HasAdminAccess(owner))
            {
                try
                {
                    var library = await session.WithAdminTokenAsync(owner, api.GetDocumentsAsync, ct);
                    var ids = library.Documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                    foreach (var entry in processed.Where(i => !ids.Contains(i.Result.DocumentId!)))
                        entry.Result = entry.Result with { State = UploadState.Warning, Detail = entry.Result.Detail + " No entry appeared in the refreshed index." };
                    Notice = "Content library refreshed.";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException or OperationCanceledException)
                {
                    Notice = "Processing finished, but the library could not be refreshed. Open Content to check the index.";
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { Busy = false; }
    }

    private async Task<UploadResult> UploadAsync(IBrowserFile file, IProgress<int> progress, CancellationToken ct)
    {
        if (!session.HasAdminAccess(owner) || !await api.CanSubmitUploadsAsync(ct))
            return new(UploadState.Blocked, "Submission is blocked. Check administrator access and API availability.");

        return await session.WithAdminTokenAsync(owner, async (token, cancel) =>
        {
            Stream input;
            try { input = file.OpenReadStream(MaxBytes, cancel); }
            catch (IOException) { return new UploadResult(UploadState.Retryable, "Could not open the selected file. Retry or select it again."); }
            await using var stream = input;
            var result = await api.UploadDocumentAsync(token, file.Name, stream, file.Size, progress, cancel);
            if (string.IsNullOrWhiteSpace(result.DocumentId) || result.ChunkCount < 0 || result.VectorCount < 0)
                return new UploadResult(UploadState.Unconfirmed, "The API returned an invalid ingestion receipt. Check Content before resubmitting.");
            return result.ChunkCount > 0 && result.VectorCount > 0
                ? new UploadResult(UploadState.Ingested, $"Document ingested: {result.ChunkCount} chunks, {result.VectorCount} vectors.", result.DocumentId)
                : new UploadResult(UploadState.Warning, "Processed without searchable chunks or vectors. Check Content.", result.DocumentId);
        }, ct);
    }

    public void Dispose()
    {
        if (lifetime.IsCancellationRequested) return;
        Available = false;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

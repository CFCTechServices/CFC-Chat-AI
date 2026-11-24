using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace CFC.ChatAI.Web.Services;

public sealed partial class FastApiClient
{
    public const string UploadClientKey = "AdminUpload";
    public long MaxUploadBytes => uploadPolicy?.MaxFileBytes ?? AdminUploadPolicy.DefaultMaxBytes;

    internal async Task<bool> CanSubmitUploadsAsync(CancellationToken ct)
    {
        if (uploadPolicy?.Approves(BaseAddress) != true) return false;
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var schema = await SendAsync<JsonElement>(HttpMethod.Get, "openapi.json", probe.Token);
            return AdminUploadPolicy.DeclaresBearerOnUploads(schema);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task<UploadResult> SubmitUploadAsync(SupabaseSessionService session, string owner, UploadArea area,
        IBrowserFile file, IProgress<int> progress, CancellationToken ct)
    {
        var invalid = AdminUploadPolicy.Validate(area, file.Name, file.Size, MaxUploadBytes);
        if (invalid is not null) return new(UploadState.Invalid, invalid);
        if (!session.HasAdminAccess(owner) || !await CanSubmitUploadsAsync(ct))
            return new(UploadState.Blocked, "Submission is blocked. Verify administrator access and backend approval.");

        return await session.WithAdminTokenAsync(owner, async (token, cancel) =>
        {
            Stream input;
            try { input = file.OpenReadStream(MaxUploadBytes, cancel); }
            catch (IOException) { return new UploadResult(UploadState.Retryable, "Could not open the selected file. Retry or select it again."); }
            await using var stream = input;
            using var form = new MultipartFormDataContent { HeaderEncodingSelector = (_, _) => Encoding.UTF8 };
            var part = new UploadFileContent(stream, file.Size, progress);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            part.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"file\"; filename=\"{file.Name}\"");
            form.Add(part);

            if (AdminUploadPolicy.IsVideo(file.Name))
            {
                var slug = AdminUploadPolicy.VideoSlug(file.Name);
                form.Add(new StringContent(slug), "slug");
                form.Add(new StringContent("small"), "model");
                var video = await SendAsync<VideoReceipt>(HttpMethod.Post, "api/videos/upload", cancel, Bearer(token), form);
                return video.Ok && video.Slug == slug && !string.IsNullOrWhiteSpace(video.Transcripts?.Txt)
                    ? new UploadResult(UploadState.Transcribed, "Video uploaded and transcribed.", slug)
                    : new UploadResult(UploadState.Unconfirmed, "Python did not confirm transcription. Check Content before resubmitting.");
            }

            var doc = await SendAsync<FileReceipt>(HttpMethod.Post, "api/files/upload", cancel, Bearer(token), form);
            if (doc.Ingestion is null)
                return new UploadResult(UploadState.Unconfirmed, "No ingestion result was returned. Check Content before resubmitting.");
            if (!doc.Ingestion.Success)
                return new UploadResult(UploadState.Failed, "Python could not ingest this file. The original may already be stored. Check Content before resubmitting.");
            if (string.IsNullOrWhiteSpace(doc.Ingestion.DocId))
                return new UploadResult(UploadState.Unconfirmed, "Ingestion returned no document ID. Refresh Content to check the result.");
            if (doc.Ingestion.ChunksProcessed is null or < 0)
                return new UploadResult(UploadState.Warning, "Ingestion returned no valid chunk count. Verify the indexed content.", doc.Ingestion.DocId);
            if (doc.Ingestion.ChunksProcessed == 0)
                return new UploadResult(UploadState.Warning, "Processed with zero searchable chunks.", doc.Ingestion.DocId);
            return doc.Supabase?.Stored == true
                ? new UploadResult(UploadState.Ingested, "Document ingested and original stored.", doc.Ingestion.DocId)
                : new UploadResult(UploadState.Warning, "Document ingested. Storage of the downloadable original was not confirmed.", doc.Ingestion.DocId);
        }, ct);
    }

    private sealed record FileReceipt(IngestionReceipt? Ingestion = null, StorageReceipt? Supabase = null);
    private sealed record IngestionReceipt(bool Success, [property: JsonPropertyName("doc_id")] string? DocId = null,
        [property: JsonPropertyName("chunks_processed")] int? ChunksProcessed = null);
    private sealed record StorageReceipt(bool Stored);
    private sealed record VideoReceipt(bool Ok, string? Slug = null, TranscriptReceipt? Transcripts = null);
    private sealed record TranscriptReceipt(string? Txt = null);

    private sealed class UploadFileContent(Stream source, long length, IProgress<int> progress) : HttpContent
    {
        protected override bool TryComputeLength(out long size) { size = length; return true; }
        protected override Task SerializeToStreamAsync(Stream destination, TransportContext? context) =>
            SerializeToStreamAsync(destination, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream destination, TransportContext? context, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            long sent = 0;
            var percent = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                sent += read;
                if (sent > length) throw new IOException("File length changed.");
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                var next = (int)(100.0 * sent / length);
                if (next != percent) { percent = next; progress.Report(percent); }
            }
            if (sent != length) throw new IOException("File transfer was incomplete.");
        }
    }
}

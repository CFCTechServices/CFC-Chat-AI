using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CFC.ChatAI.Web.Models;
using CFC.ChatAI.Web.Services.Admin;

namespace CFC.ChatAI.Web.Services.Api;

public sealed partial class ChatApiClient
{
    private const string DocumentsPath = "api/admin/documents";
    public long MaxUploadBytes => uploadPolicy?.MaxFileBytes ?? AdminUploadPolicy.DefaultMaxBytes;

    public Task<AdminDocumentList> GetDocumentsAsync(string token, CancellationToken ct) =>
        SendAsync<AdminDocumentList>(HttpMethod.Get, DocumentsPath, ct, Bearer(token));

    public Task<DocumentDownload> GetDocumentDownloadAsync(string token, string id, CancellationToken ct) =>
        SendAsync<DocumentDownload>(HttpMethod.Get, ResourcePath(DocumentsPath, id) + "/download", ct, Bearer(token));

    public Task<DocumentDeletion> DeleteDocumentAsync(string token, string id, CancellationToken ct) =>
        SendAsync<DocumentDeletion>(HttpMethod.Delete, ResourcePath(DocumentsPath, id), ct, Bearer(token));

    public async Task<DocumentReplacement> ReplaceDocumentAsync(string token, string id, string filename,
        Stream file, long length, CancellationToken ct)
    {
        using var form = DocumentForm(filename, file, length);
        return await SendAsync<DocumentReplacement>(HttpMethod.Put, ResourcePath(DocumentsPath, id) + "/replace", ct, Bearer(token), form);
    }

    internal async Task<bool> CanSubmitUploadsAsync(CancellationToken ct)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(TimeSpan.FromSeconds(10));
        try { return await GetAuthConfigAsync(probe.Token) is { Backend: "dotnet", DocumentUploads: "true" }; }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException or OperationCanceledException)
        { return false; }
    }

    internal async Task<DocumentUploadReceipt> UploadDocumentAsync(string token, string filename, Stream file,
        long length, IProgress<int> progress, CancellationToken ct)
    {
        using var form = DocumentForm(filename, file, length, progress);
        return await SendAsync<DocumentUploadReceipt>(HttpMethod.Post, "api/files/upload", ct, Bearer(token), form);
    }

    private MultipartFormDataContent DocumentForm(string filename, Stream file, long length, IProgress<int>? progress = null)
    {
        if (AdminUploadPolicy.Validate(UploadArea.Single, filename, length, MaxUploadBytes) is { } error)
            throw new ArgumentException(error, nameof(filename));

        var form = new MultipartFormDataContent { HeaderEncodingSelector = (_, _) => Encoding.UTF8 };
        var part = new DocumentFileContent(file, length, progress);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // Match browser FormData: the API reads the raw UTF-8 filename, not an RFC 2047 encoded word.
        part.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"file\"; filename=\"{filename}\"");
        form.Add(part);
        return form;
    }

    internal sealed record DocumentUploadReceipt(
        [property: JsonPropertyName("document_id")] string DocumentId,
        [property: JsonPropertyName("chunk_count")] int ChunkCount,
        [property: JsonPropertyName("vector_count")] int VectorCount);

    private sealed class DocumentFileContent(Stream source, long length, IProgress<int>? progress) : HttpContent
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
                if (next != percent) { percent = next; progress?.Report(percent); }
            }
            if (sent != length) throw new IOException("File transfer was incomplete.");
        }
    }
}

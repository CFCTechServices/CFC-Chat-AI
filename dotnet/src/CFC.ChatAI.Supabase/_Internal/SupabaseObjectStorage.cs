using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Storage;

namespace CFC.ChatAI.Supabase.Internal;

internal sealed class SupabaseObjectStorage(SupabaseTransport transport, SupabaseOptions options) : IObjectStorage
{
    private const int DeleteBatchSize = 100;
    private static readonly TimeSpan MaximumSignedUrlLifetime = TimeSpan.FromHours(1);

    public async Task<StoredObject> UploadAsync(
        ObjectKey key,
        Stream content,
        UploadOptions uploadOptions,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(uploadOptions);
        if (!content.CanRead) throw new ArgumentException("Upload stream must be readable.", nameof(content));

        var bucket = GetBucket(key.Area);
        var uri = ObjectUri("object", bucket, key.Path);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StreamContent(new BorrowedStream(content)),
        };
        request.Headers.TryAddWithoutValidation("x-upsert", uploadOptions.Overwrite ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(uploadOptions.ContentType))
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(uploadOptions.ContentType);

        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        return new StoredObject(key, uploadOptions.ContentType);
    }

    public async Task<DownloadedObject> DownloadAsync(
        ObjectKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var uri = ObjectUri("object", GetBucket(key.Area), key.Path);
        var request = SupabaseTransport.Request(HttpMethod.Get, uri);
        HttpResponseMessage response;
        try
        {
            response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        request.Dispose();
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return new DownloadedObject(
                key,
                new ResponseOwnedStream(stream, response),
                mediaType,
                response.Content.Headers.ContentLength);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<Page<StorageEntry>> ListAsync(
        ObjectPrefix prefix,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ValidatePrefix(prefix);
        ArgumentNullException.ThrowIfNull(page);
        var uri = transport.StorageUri($"object/list/{SupabaseTransport.Escape(GetBucket(prefix.Area))}");
        var requestBody = new
        {
            prefix = prefix.Path.TrimEnd('/'),
            limit = page.Limit,
            offset = page.Offset,
            sortBy = new { column = "name", order = "asc" },
        };
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, requestBody);
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var rows = await SupabaseTransport.ReadJsonAsync<List<StorageListRow>>(response, cancellationToken)
            .ConfigureAwait(false);

        var basePath = prefix.Path.TrimEnd('/');
        var entries = rows.Select(row =>
        {
            var objectPath = string.IsNullOrEmpty(basePath) ? row.Name : $"{basePath}/{row.Name}";
            var isFolder = row.Id is null && row.Metadata is null;
            return new StorageEntry(
                new ObjectKey(prefix.Area, objectPath),
                isFolder,
                row.Metadata?.MimeType,
                row.Metadata?.Size);
        }).ToArray();
        return new Page<StorageEntry>(entries, rows.Count == page.Limit);
    }

    public async Task DeleteAsync(
        IReadOnlyList<ObjectKey> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys) ValidateKey(key);

        foreach (var group in keys.Distinct().GroupBy(key => key.Area))
        {
            foreach (var batch in group.Chunk(DeleteBatchSize))
            {
                var uri = transport.StorageUri($"object/{SupabaseTransport.Escape(GetBucket(group.Key))}");
                using var request = SupabaseTransport.JsonRequest(
                    HttpMethod.Delete, uri, new { prefixes = batch.Select(key => key.Path).ToArray() });
                using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<Uri> CreateSignedUrlAsync(
        ObjectKey key,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumSignedUrlLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Signed URL lifetime must be positive and no more than one hour.");
        var seconds = checked((long)Math.Ceiling(lifetime.TotalSeconds));
        var uri = ObjectUri("object/sign", GetBucket(key.Area), key.Path);
        using var request = SupabaseTransport.JsonRequest(HttpMethod.Post, uri, new { expiresIn = seconds });
        using var response = await transport.SendServiceAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await SupabaseTransport.ReadJsonAsync<SignedUrlResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        var signedUrl = result.SignedUrl;
        if (string.IsNullOrWhiteSpace(signedUrl) && result.AdditionalData is not null &&
            result.AdditionalData.TryGetValue("signedURL", out var alternate) && alternate.ValueKind == System.Text.Json.JsonValueKind.String)
            signedUrl = alternate.GetString();
        if (string.IsNullOrWhiteSpace(signedUrl))
            throw new InvalidDataException("Supabase did not return a signed URL.");
        if (Uri.TryCreate(signedUrl, UriKind.Absolute, out var absolute)) return absolute;
        var relativePath = signedUrl.TrimStart('/');
        return relativePath.StartsWith("storage/v1/", StringComparison.Ordinal)
            ? new Uri(new Uri(options.Url.TrimEnd('/') + "/"), relativePath)
            : transport.StorageUri(relativePath);
    }

    public Uri GetPublicUrl(ObjectKey key)
    {
        ValidateKey(key);
        return ObjectUri("object/public", GetBucket(key.Area), key.Path);
    }

    private string GetBucket(StorageArea area) => area switch
    {
        StorageArea.Documents => options.DocumentsBucket,
        StorageArea.Videos => options.VideosBucket,
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    private Uri ObjectUri(string endpoint, string bucket, string path)
    {
        var encodedPath = string.Join('/', path.Split('/').Select(SupabaseTransport.Escape));
        return transport.StorageUri($"{endpoint}/{SupabaseTransport.Escape(bucket)}/{encodedPath}");
    }

    private static void ValidateKey(ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.Path) || key.Path.StartsWith('/') || key.Path.Contains('\\'))
            throw new ArgumentException("Storage object paths must be non-empty relative slash-delimited paths.", nameof(key));
        if (key.Path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Storage object paths cannot contain empty, '.' or '..' segments.", nameof(key));
        if (!Enum.IsDefined(key.Area)) throw new ArgumentOutOfRangeException(nameof(key));
    }

    private static void ValidatePrefix(ObjectPrefix prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Path.Contains('\\') || prefix.Path.StartsWith('/') ||
            prefix.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            throw new ArgumentException("Storage prefixes must be relative and cannot traverse directories.", nameof(prefix));
        if (!Enum.IsDefined(prefix.Area)) throw new ArgumentOutOfRangeException(nameof(prefix));
    }

    private sealed class StorageListRow
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("metadata")] public StorageMetadata? Metadata { get; init; }
    }

    private sealed class StorageMetadata
    {
        [JsonPropertyName("mimetype")] public string? MimeType { get; init; }
        [JsonPropertyName("size")] public long? Size { get; init; }
    }

    private sealed class SignedUrlResponse
    {
        [JsonPropertyName("signedUrl")] public string? SignedUrl { get; init; }
        [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
    }

    private sealed class BorrowedStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) response.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            response.Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}

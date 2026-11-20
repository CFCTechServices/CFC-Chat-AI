using System.Text.Json;
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Pinecone.Contracts;
using CFC.ChatAI.Supabase.Chunks;
using CFC.ChatAI.Supabase.Common;
using CFC.ChatAI.Supabase.Storage;
using CFC.ChatAI.VideoProcessor;
using Microsoft.Extensions.DependencyInjection;

namespace CFC.ChatAI.DocumentHandler.Tests;

public sealed class DocumentHandlerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a%2fb", false)]
    [InlineData("a\nb", false)]
    [InlineData("café", true)]
    [InlineData("cafe\u0301", true)]
    [InlineData("日本語𐐨", true)]
    [InlineData("a\u202Eb", false)]
    [InlineData("a／b", false)]
    [InlineData("😀", false)]
    [InlineData("a-b_c.1", true)]
    [InlineData("...", true)]
    public void Document_id_validation_matches_route_and_storage_contract(string? value, bool valid) =>
        Assert.Equal(valid, DocumentId.IsValid(value));

    [Fact]
    public async Task Invalid_document_ids_are_rejected_before_provider_access()
    {
        Assert.True(DocumentId.IsValid(new string('a', 128)));
        Assert.False(DocumentId.IsValid(new string('a', 129)));
        var chunks = new FakeChunkRepository();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, chunks, new FakeObjectStorage(), new FakePinecone());
        foreach (var id in new[] { "", ".", "..", "a/b", "a%2fb", "a\u202Eb", new string('a', 129) })
            await Assert.ThrowsAsync<ArgumentException>(() => handler.DeleteAsync(id));
        Assert.Equal(0, chunks.MetadataReads);
    }

    [Fact]
    public async Task Ingest_and_delete_document_coordinates_all_providers_and_keeps_input_open()
    {
        var chunks = new FakeChunkRepository();
        var storage = new FakeObjectStorage();
        var pinecone = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), new FakeVideoProcessor(), chunks, storage, pinecone);
        await using var input = new MemoryStream("hello world"u8.ToArray());

        var result = await handler.IngestAsync(new DocumentHandlerInput("sample.txt", input));

        Assert.Equal("sample", result.DocumentId);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(1, result.VectorCount);
        Assert.Equal(StorageArea.Documents, result.SourceObject.Area);
        Assert.True(input.CanRead);
        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(storage.Objects[result.SourceObject]));
        Assert.Equal("sample", chunks.Rows.Single().DocumentId);
        Assert.Equal("sample", pinecone.Records.Single().Metadata!["doc_id"].GetString());

        var deleted = await handler.DeleteAsync("sample");

        Assert.True(deleted.Found);
        Assert.Equal(1, deleted.ChunkCount);
        Assert.Equal(1, deleted.StorageObjectCount);
        Assert.Empty(chunks.Rows);
        Assert.Empty(pinecone.Records);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task Unicode_ingest_and_delete_use_provider_safe_keys_and_preserve_exact_ids()
    {
        const string id = "café-guide-v2";
        var chunks = new FakeChunkRepository(); var storage = new FakeObjectStorage(); var vectors = new FakePinecone();
        var processor = new DocumentProcessor.DocumentProcessor(new DocumentProcessorOptions());
        var handler = CreateHandler(processor, null, chunks, storage, vectors);
        await using var input = new MemoryStream("Subject: Sample\r\n\r\nEmail body."u8.ToArray());
        var result = await handler.IngestAsync(new DocumentHandlerInput("Cafe\u0301 Guide V2.EML", input));

        Assert.Equal(id, result.DocumentId);
        Assert.Equal(id, chunks.Rows.Single().DocumentId);
        Assert.Equal(id, vectors.Records.Single().Metadata!["doc_id"].GetString());
        Assert.Equal("docs/!u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml", result.SourceObject.Path);
        Assert.Equal(input.ToArray(), storage.Objects[result.SourceObject]);
        storage.Objects[new ObjectKey(StorageArea.Documents, $"docs/{id}/original/{id}.eml")] = [];
        var neighbor = new ObjectKey(StorageArea.Documents, "docs/cafe\u0301-guide-v2/original/legacy.eml");
        storage.Objects[neighbor] = [];

        var deleted = await handler.DeleteAsync(id);
        Assert.True(deleted.Found);
        Assert.Equal(2, deleted.StorageObjectCount);
        Assert.Empty(chunks.Rows);
        Assert.Empty(vectors.Records);
        Assert.Equal(neighbor, Assert.Single(storage.Objects).Key);
    }

    [Fact]
    public async Task Delete_handles_pages_nested_artifacts_and_neighboring_ids()
    {
        var chunks = new FakeChunkRepository(); var storage = new FakeObjectStorage(); var vectors = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, chunks, storage, vectors);
        for (var i = 0; i < 1001; i++)
        {
            chunks.Rows.Add(new Chunk($"chunk-{i:D4}", "sample", "text", SourceType: "document"));
            vectors.Records.Add(new PineconeRecord($"chunk-{i:D4}", "text", null));
            storage.Objects[new ObjectKey(StorageArea.Documents, $"docs/sample/images/{i:D4}.png")] = [];
        }
        var neighbor = new ObjectKey(StorageArea.Documents, "docs/sample-other/original/file.txt");
        storage.Objects[neighbor] = [];
        storage.Objects[new ObjectKey(StorageArea.Documents, "docs/sample/original/file.txt")] = [];
        var result = await handler.DeleteAsync("sample");
        Assert.True(result.Found); Assert.Equal(1001, result.ChunkCount); Assert.Equal(1002, result.StorageObjectCount);
        Assert.Empty(chunks.Rows); Assert.Empty(vectors.Records); Assert.Single(storage.Objects);
        Assert.True(storage.Objects.ContainsKey(neighbor));
        Assert.False((await handler.DeleteAsync("sample")).Found);
    }

    [Theory]
    [InlineData("vector", false)]
    [InlineData("vector", true)]
    [InlineData("storage", false)]
    [InlineData("storage", true)]
    [InlineData("chunks", false)]
    [InlineData("chunks", true)]
    public async Task Delete_failures_are_reported_and_chunk_ids_are_kept_until_final_stage(string stage, bool after)
    {
        var chunks = new FakeChunkRepository(); var storage = new FakeObjectStorage(); var vectors = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, chunks, storage, vectors);
        await using var input = new MemoryStream("hello"u8.ToArray());
        await handler.IngestAsync(new DocumentHandlerInput("sample.txt", input));
        if (stage == "vector") vectors.FailDelete = after ? 2 : 1;
        if (stage == "storage") storage.FailDelete = after ? 2 : 1;
        if (stage == "chunks") chunks.FailDelete = after ? 2 : 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.DeleteAsync("sample"));
        if (stage != "chunks" || !after) Assert.Single(chunks.Rows);
        vectors.FailDelete = storage.FailDelete = chunks.FailDelete = 0;
        if (stage == "chunks" && after)
            Assert.False((await handler.DeleteAsync("sample")).Found); // ambiguous final response requires reconciliation
        else
            Assert.True((await handler.DeleteAsync("sample")).Found);
    }

    [Fact]
    public async Task Delete_rejects_invalid_storage_listing_without_deleting_neighbors()
    {
        var chunks = new FakeChunkRepository(); var storage = new FakeObjectStorage { ReturnInvalidChild = true }; var vectors = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, chunks, storage, vectors);
        await using var input = new MemoryStream("hello"u8.ToArray());
        await handler.IngestAsync(new DocumentHandlerInput("sample.txt", input));
        var neighbor = new ObjectKey(StorageArea.Documents, "docs/sample-other/file.txt");
        storage.Objects[neighbor] = [];
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.DeleteAsync("sample"));
        Assert.True(storage.Objects.ContainsKey(neighbor));
        Assert.Single(chunks.Rows);
    }

    [Fact]
    public async Task Delete_rejects_video_and_unknown_metadata_without_mutating_stores()
    {
        var chunks = new FakeChunkRepository(); var storage = new FakeObjectStorage(); var vectors = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), new FakeVideoProcessor(), chunks, storage, vectors);
        await using var input = new MemoryStream([1, 2, 3]);
        await handler.IngestAsync(new DocumentHandlerInput("clip.mp4", input));
        await Assert.ThrowsAsync<UnsupportedDeletionException>(() => handler.DeleteAsync("clip"));
        Assert.Single(chunks.Rows); Assert.Single(storage.Objects); Assert.Single(vectors.Records);
        chunks.Rows.Clear();
        Assert.False((await handler.DeleteAsync("clip")).Found);
        Assert.Single(storage.Objects); Assert.Single(vectors.Records);
    }

    [Fact]
    public async Task Video_upload_uses_video_processor_video_storage_and_video_metadata()
    {
        var documentProcessor = new FakeDocumentProcessor();
        var videoProcessor = new FakeVideoProcessor();
        var chunks = new FakeChunkRepository();
        var storage = new FakeObjectStorage();
        var pinecone = new FakePinecone();
        var handler = CreateHandler(documentProcessor, videoProcessor, chunks, storage, pinecone);
        await using var input = new MemoryStream([1, 2, 3]);

        var result = await handler.IngestAsync(new DocumentHandlerInput("clip.mp4", input));

        Assert.Equal(0, documentProcessor.CallCount);
        Assert.Equal(1, videoProcessor.CallCount);
        Assert.Equal(StorageArea.Videos, result.SourceObject.Area);
        Assert.Equal("video", chunks.Rows.Single().SourceType);
        Assert.NotNull(chunks.Rows.Single().VideoUrl);
    }

    [Fact]
    public async Task Pinecone_failure_rolls_back_chunk_rows()
    {
        var chunks = new FakeChunkRepository();
        var storage = new FakeObjectStorage();
        var pinecone = new FakePinecone { ThrowOnUpsert = true };
        var handler = CreateHandler(new FakeDocumentProcessor(), new FakeVideoProcessor(), chunks, storage, pinecone);
        await using var input = new MemoryStream("hello"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.IngestAsync(new DocumentHandlerInput("sample.txt", input)));

        Assert.Empty(chunks.Rows);
        Assert.Empty(pinecone.Records);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task Storage_failure_compensates_and_failed_compensation_is_reported()
    {
        var chunks = new FakeChunkRepository();
        var pinecone = new FakePinecone();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, chunks, new FakeObjectStorage { ThrowOnUpload = true }, pinecone);
        await using var input = new MemoryStream("hello"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.IngestAsync(new DocumentHandlerInput("sample.txt", input)));
        Assert.Empty(chunks.Rows);
        Assert.Empty(pinecone.Records);
        chunks.ThrowOnDelete = true;
        input.Position = 0;
        await Assert.ThrowsAsync<AggregateException>(() => handler.IngestAsync(new DocumentHandlerInput("sample.txt", input)));
    }

    [Fact]
    public async Task Documents_resolve_without_video_and_video_fails_explicitly()
    {
        var handler = CreateHandler(new FakeDocumentProcessor(), null, new FakeChunkRepository(), new FakeObjectStorage(), new FakePinecone());
        await using var input = new MemoryStream("hello"u8.ToArray());
        Assert.Equal("sample", (await handler.IngestAsync(new DocumentHandlerInput("sample.txt", input))).DocumentId);
        input.Position = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.IngestAsync(new DocumentHandlerInput("clip.mp4", input)));
        Assert.Contains("Video processing not configured", error.Message);
    }

    [Fact]
    public async Task Stage_enforces_actual_size_and_cleans_up_on_failure_and_cancellation()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "cfc-upload-*.tmp").ToHashSet();
        var handler = CreateHandler(new FakeDocumentProcessor(), null, new FakeChunkRepository(), new FakeObjectStorage(), new FakePinecone(), 3);
        await using var input = new MemoryStream("hello"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.IngestAsync(new DocumentHandlerInput("sample.txt", input)));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.IngestAsync(new DocumentHandlerInput("sample.txt", input), canceled.Token));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "cfc-upload-*.tmp").Except(before));
    }

    private static IDocumentHandler CreateHandler(
        IDocumentProcessor documentProcessor,
        FakeVideoProcessor? videoProcessor,
        FakeChunkRepository chunks,
        FakeObjectStorage storage,
        FakePinecone pinecone,
        long maxUploadBytes = 500L * 1024 * 1024)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentProcessor>(documentProcessor);
        if (videoProcessor is not null) services.AddSingleton<IVideoProcessor>(videoProcessor);
        services.AddSingleton<IChunkRepository>(chunks);
        services.AddSingleton<IObjectStorage>(storage);
        services.AddSingleton<IPineconeService>(pinecone);
        services.AddDocumentHandling(o => o.MaxUploadBytes = maxUploadBytes);
        return services.BuildServiceProvider().GetRequiredService<IDocumentHandler>();
    }

    private sealed class FakeDocumentProcessor : IDocumentProcessor
    {
        public int CallCount { get; private set; }

        public async Task<DocumentProcessingResult> ProcessAsync(
            DocumentInput document,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await document.Content.CopyToAsync(Stream.Null, cancellationToken);
            return new DocumentProcessingResult(
                "sample", document.FileName, "document", [],
                [new DocumentChunk("doc-chunk", "section-1", "Body", "document text")]);
        }
    }

    private sealed class FakeVideoProcessor : IVideoProcessor
    {
        public int CallCount { get; private set; }

        public async Task<VideoProcessingResult> ProcessAsync(
            VideoInput video,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await video.Content.CopyToAsync(Stream.Null, cancellationToken);
            return new VideoProcessingResult(
                "clip", video.FileName, "video", TimeSpan.FromSeconds(1), [],
                [new VideoChunk("video-chunk", "transcript", "Transcript", "spoken words")]);
        }
    }

    private sealed class FakeChunkRepository : IChunkRepository
    {
        public List<Chunk> Rows { get; } = [];
        public bool ThrowOnDelete { get; set; }
        public int FailDelete { get; set; }
        public int MetadataReads { get; private set; }

        public Task UpsertAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(existing => chunks.Any(chunk => chunk.Id == existing.Id));
            Rows.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, Chunk>> GetByIdsAsync(
            IReadOnlyList<string> chunkIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, Chunk>>(
                Rows.Where(row => chunkIds.Contains(row.Id)).ToDictionary(row => row.Id));

        public Task<ChunkIdPage> ListIdsForDocumentAsync(
            string documentId,
            PageRequest page,
            CancellationToken cancellationToken = default)
        {
            var ids = Rows.Where(row => row.DocumentId == documentId).Select(row => row.Id)
                .Order(StringComparer.Ordinal).Skip(page.Offset).Take(page.Limit).ToArray();
            return Task.FromResult(new ChunkIdPage(ids, ids.Length == page.Limit));
        }

        public Task<DocumentMetadata?> GetDocumentMetadataAsync(
            string documentId,
            CancellationToken cancellationToken = default)
        {
            MetadataReads++;
            var row = Rows.FirstOrDefault(item => item.DocumentId == documentId);
            return Task.FromResult(row is null ? null : new DocumentMetadata(documentId, row.Source, row.SourceType));
        }

        public Task<Page<DocumentSummary>> ListDocumentSummariesAsync(
            PageRequest page,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteByIdsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDelete || FailDelete == 1) throw new InvalidOperationException("delete failed");
            Rows.RemoveAll(row => chunkIds.Contains(row.Id));
            if (FailDelete == 2) throw new InvalidOperationException("delete response lost");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeObjectStorage : IObjectStorage
    {
        public Dictionary<ObjectKey, byte[]> Objects { get; } = [];
        public bool ThrowOnUpload { get; init; }
        public bool ReturnInvalidChild { get; init; }
        public int FailDelete { get; set; }

        public async Task<StoredObject> UploadAsync(
            ObjectKey key,
            Stream content,
            UploadOptions options,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpload) throw new InvalidOperationException("storage failed");
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Objects[key] = memory.ToArray();
            return new StoredObject(key, options.ContentType, memory.Length);
        }

        public Task<DownloadedObject> DownloadAsync(ObjectKey key, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(Objects[key], writable: false);
            return Task.FromResult(new DownloadedObject(key, stream, null, stream.Length));
        }

        public Task<Page<StorageEntry>> ListAsync(
            ObjectPrefix prefix,
            PageRequest page,
            CancellationToken cancellationToken = default)
        {
            if (ReturnInvalidChild)
                return Task.FromResult(new Page<StorageEntry>([new StorageEntry(new ObjectKey(prefix.Area, "docs/sample-other/file.txt"), false)], false));
            var root = prefix.Path.TrimEnd('/');
            var children = new Dictionary<string, StorageEntry>(StringComparer.Ordinal);
            foreach (var key in Objects.Keys.Where(key => key.Area == prefix.Area && key.Path.StartsWith(root + "/", StringComparison.Ordinal)))
            {
                var remainder = key.Path[(root.Length + 1)..];
                var slash = remainder.IndexOf('/');
                var name = slash < 0 ? remainder : remainder[..slash];
                var childPath = $"{root}/{name}";
                children[name] = slash < 0
                    ? new StorageEntry(key, false)
                    : new StorageEntry(new ObjectKey(prefix.Area, childPath), true);
            }

            var items = children.Values.OrderBy(item => item.Key.Path, StringComparer.Ordinal)
                .Skip(page.Offset).Take(page.Limit + 1).ToArray();
            var hasMore = items.Length > page.Limit;
            if (hasMore) Array.Resize(ref items, page.Limit);
            return Task.FromResult(new Page<StorageEntry>(items, hasMore));
        }

        public Task DeleteAsync(IReadOnlyList<ObjectKey> keys, CancellationToken cancellationToken = default)
        {
            if (FailDelete == 1) throw new InvalidOperationException("storage delete failed");
            foreach (var key in keys) Objects.Remove(key);
            if (FailDelete == 2) throw new InvalidOperationException("storage response lost");
            return Task.CompletedTask;
        }

        public Task<Uri> CreateSignedUrlAsync(ObjectKey key, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetPublicUrl(key));

        public Uri GetPublicUrl(ObjectKey key) => new($"https://storage.example/{key.Path}");
    }

    private sealed class FakePinecone : IPineconeService
    {
        public List<PineconeRecord> Records { get; } = [];
        public bool ThrowOnUpsert { get; init; }
        public int FailDelete { get; set; }

        public Task<PineconeSearchResult> SearchAsync(
            string query,
            int topK = 5,
            bool includeMetadata = true,
            IReadOnlyDictionary<string, JsonElement>? filter = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> UpsertAsync(IReadOnlyList<PineconeRecord> records, CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpsert) throw new InvalidOperationException("Pinecone is unavailable.");
            Records.AddRange(records);
            return Task.FromResult(records.Count);
        }

        public Task DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        {
            if (FailDelete == 1) throw new InvalidOperationException("vector delete failed");
            Records.RemoveAll(record => ids.Contains(record.Id));
            if (FailDelete == 2) throw new InvalidOperationException("vector response lost");
            return Task.CompletedTask;
        }

        public Task<PineconeIndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

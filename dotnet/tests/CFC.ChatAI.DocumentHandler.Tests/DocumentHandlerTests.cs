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

    private static IDocumentHandler CreateHandler(
        FakeDocumentProcessor documentProcessor,
        FakeVideoProcessor videoProcessor,
        FakeChunkRepository chunks,
        FakeObjectStorage storage,
        FakePinecone pinecone)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentProcessor>(documentProcessor);
        services.AddSingleton<IVideoProcessor>(videoProcessor);
        services.AddSingleton<IChunkRepository>(chunks);
        services.AddSingleton<IObjectStorage>(storage);
        services.AddSingleton<IPineconeService>(pinecone);
        services.AddDocumentHandling();
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
            var row = Rows.FirstOrDefault(item => item.DocumentId == documentId);
            return Task.FromResult(row is null ? null : new DocumentMetadata(documentId, row.Source, row.SourceType));
        }

        public Task<Page<DocumentSummary>> ListDocumentSummariesAsync(
            PageRequest page,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteByIdsAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(row => chunkIds.Contains(row.Id));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeObjectStorage : IObjectStorage
    {
        public Dictionary<ObjectKey, byte[]> Objects { get; } = [];

        public async Task<StoredObject> UploadAsync(
            ObjectKey key,
            Stream content,
            UploadOptions options,
            CancellationToken cancellationToken = default)
        {
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
            foreach (var key in keys) Objects.Remove(key);
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
            Records.RemoveAll(record => ids.Contains(record.Id));
            return Task.CompletedTask;
        }

        public Task<PineconeIndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

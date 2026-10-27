# Document handling

`CFC.ChatAI.DocumentHandler` is the application workflow for ingesting and deleting searchable documents and videos. It composes the independent `DocumentProcessor` and `VideoProcessor` contracts with Supabase chunk/object persistence and Pinecone vector indexing. Provider adapters remain isolated: this package owns the cross-provider workflow.

## Responsibilities and boundaries

- `IDocumentHandler.IngestAsync` accepts one filename and readable stream, selects the appropriate processor, writes searchable chunks to Supabase, embeds/indexes them through Pinecone, and retains the original upload in Supabase Storage.
- `IDocumentHandler.DeleteAsync` removes vectors, chunk rows, and stored objects for a document ID.
- The input stream remains owned by the caller. The handler stages it in a temporary file and removes that file after processing. Processors also retain their documented stream ownership.
- Document formats and video formats are determined by their filename extension. Documents use `IDocumentProcessor`; the video extensions supported by `VideoProcessor` use `IVideoProcessor`. Unsupported extensions are rejected by the document processor.
- The handler does not implement HTTP endpoints, authorization, chat retrieval, bulk APIs, or a distributed transaction. Those are host/application responsibilities.

## Registration

Register processor, provider, and handler capabilities in the application composition root:

```csharp
using CFC.ChatAI.DocumentHandler;
using CFC.ChatAI.DocumentProcessor;
using CFC.ChatAI.Pinecone;
using CFC.ChatAI.Supabase;
using CFC.ChatAI.VideoProcessor;

services.AddDocumentProcessing(options =>
{
    options.MaxDocumentBytes = 50L * 1024 * 1024;
});
services.AddVideoProcessing(options =>
{
    options.SpeechEndpoint = new Uri(configuration["Speech:Endpoint"]!);
    options.SpeechApiKey = configuration["Speech:ApiKey"]!;
});
services.AddPineconeInfrastructure(options => { /* configure index and embedding model */ });
services.AddSupabaseInfrastructure(options => { /* configure URL, credentials, and buckets */ });
services.AddDocumentHandling(options =>
{
    options.MaxUploadBytes = 500L * 1024 * 1024;
});
```

The handler's default staging limit is 500 MiB. It does not override processor-level limits: document processing defaults to 50 MiB and video processing has its own video/audio limits. Configure the combined upload limit to the deployment's actual upload and Storage policy. Each service must be registered before resolving `IDocumentHandler`.

## Ingestion behavior

The handler stages the incoming stream with a bounded copy, invokes the selected processor on a seekable temporary-file stream, and maps each processor chunk to both a Supabase `Chunk` and a Pinecone `PineconeRecord`. It then upserts chunk rows, upserts vectors with the chunk IDs, and uploads the original last. The file is stored at:

- Documents: `StorageArea.Documents`, `docs/{documentId}/original/{documentId}{extension}`
- Videos: `StorageArea.Videos`, `videos/{documentId}/original/{documentId}{extension}`

The file extension is normalized to lowercase. A supplied content type is used when present; otherwise a common MIME type is inferred from the extension. Supabase chunk rows retain source name/type and section metadata. Pinecone metadata contains `doc_id`, `section_id`, `source`, and `source_type`. Video rows include a Storage public URL in `video_url`; the configured video bucket must actually be public for that URL to be directly usable. The Storage contract explicitly does not guarantee public access merely because it can construct that URL. Transcript-sidecar artifacts and per-chunk timestamps are not produced by the current video processor.

`DocumentHandlerResult` reports the processed document ID, source, chunk/vector counts, and original object's key. A filename-derived document ID is not unique by content. Re-ingesting a document whose processor creates new chunk IDs can leave previous chunks/vectors behind; replacement flows should explicitly call `DeleteAsync(documentId)` before `IngestAsync` (or otherwise implement a reviewed replacement policy). Do not assume a provider-side upsert removes old chunks.

### Failure and consistency semantics

Supabase, Pinecone, and Storage do not share a transaction. Chunk/vector writes are compensated by deleting the attempted chunk IDs if a later ingestion step fails; a compensation failure is reported together with the original error as an `AggregateException`. Compensation is best-effort and uses a non-cancelled token so that request cancellation does not prevent cleanup. Storage upload is deliberately last, but a network failure can still leave an ambiguous remote outcome; an existing original at the same key may already have been overwritten.

Deletion first reads document metadata and all chunk IDs, deletes Pinecone IDs, deletes Supabase chunk rows, and then recursively lists and removes objects under the matching `docs/{documentId}` or `videos/{documentId}` prefix. If document metadata is absent, it checks both Storage areas. If a later provider operation fails, earlier deletions are not restored. Callers should surface failures and allow an operator/retry workflow rather than claiming atomic deletion. `DocumentDeletionResult.Found` is false when no chunk metadata/IDs or source objects were found.

## Verification

From `dotnet/` run:

```sh
dotnet test tests/CFC.ChatAI.DocumentHandler.Tests/CFC.ChatAI.DocumentHandler.Tests.csproj
```

The tests use fake processors and provider contracts; they verify document ingestion/deletion coordination, video dispatch and metadata, caller stream ownership, and chunk rollback on a Pinecone failure. They do not require Supabase credentials, Pinecone credentials, FFmpeg, or Azure Speech access.

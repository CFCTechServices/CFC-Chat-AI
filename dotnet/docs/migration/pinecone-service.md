# C# chunk-level Pinecone service

## Boundary and deployment status

`IPineconeService` in `CFC.ChatAI.Rag` is the only C# production Pinecone entry point. Callers submit complete chunks, query text, typed filters, and IDs. ONNX vectors, metadata JSON, record IDs, HTTP requests, and routing stay internal. Only the RAG tests and parity harness have friend access.

There is **no database/content repository**, external hydration, file fetching, storage management, index creation, feedback persistence, or public statistics API. Existing Python endpoints and unrelated persistence are unchanged. The C# API currently has only a liveness endpoint; it does not register this dependency until a Pinecone-consuming endpoint is migrated. Liveness must remain independent of model/Pinecone readiness.

This implementation prepares the service and tests, **not a live data cutover**. Before moving callers:

1. Provision a dedicated new/versioned **dense, 384-dimensional cosine** index (or intentional shared document/video index).
2. Pin and parity-test the exact ONNX export/vocabulary; assign an immutable model identity, preferably including both file SHA256 digests.
3. Explicitly attest that each target was rebuilt or verified for that identity in configuration. Dimension equality is not an embedding compatibility attestation. The service checks the configured identity and each record identity; it does not infer model compatibility from index stats or verify operator-provided digests against files.
4. Re-ingest **from upstream chunk production**, supplying every required content/citation field. Old minimal Python records are not usable; there is no hydration fallback.
5. Test real document **and video** chunks and citations against metadata limits. The offline test includes the repository's full Beef Model guide as a conservative document feasibility sample; that does not guarantee every production chunk/video will fit.
6. Reconcile Python's video writes to the document index versus its configured retrieval target. Move video ingestion/retrieval together. Compare representative Python/C# results and verify deletion/replacement recovery before retiring old paths.

## Registration and lifetime

Use the registration extension when migrating a caller:

```csharp
using CFC.ChatAI.Rag;

builder.Services.AddPineconeService(builder.Configuration, builder.Environment.ContentRootPath);
// Inject IPineconeService, not the concrete implementation or any internal adapter.
```

Registration validates settings but does no network I/O. One lazy, serialized model owner serves both collections. DI disposes it exactly once. No per-chunk `Task.Run` is used; cancellation is checked before and after synchronous inference but cannot interrupt `InferenceSession.Run`. Host/readiness initialization is lazy, asynchronous, and cached after successful checks. Health explicitly refreshes dependency checks. Successful searches do **not** request stats on every chat.

`GetHealthAsync` reports model readiness separately from target availability/dimension/metric. Each distinct configured target is checked through control-plane description **and data-plane stats**. This is not proof of end-to-end retrieval correctness. Health diagnostics and operation exceptions are sanitized.

### Configuration

Hierarchical keys take precedence over explicitly mapped legacy keys. Pass an `IConfiguration` containing the relevant environment provider; .NET does not load the repository's `.env` file automatically. No database settings are needed:

| Hierarchical key | Legacy environment variable |
| --- | --- |
| `Pinecone:ApiKey` | `PINECONE_API_KEY` |
| `Pinecone:Documents:IndexName` | `PINECONE_INDEX_NAME` |
| `Pinecone:Documents:Host` | `PINECONE_INDEX_HOST` |
| `Pinecone:Documents:Namespace` | `PINECONE_NAMESPACE` |
| `Pinecone:Documents:EmbeddingModelIdentity` | `PINECONE_DOCUMENT_MODEL_IDENTITY` |
| `Pinecone:Videos:IndexName` | `PINECONE_VIDEO_INDEX_NAME` |
| `Pinecone:Videos:Host` | `PINECONE_VIDEO_INDEX_HOST` |
| `Pinecone:Videos:Namespace` | `PINECONE_VIDEO_NAMESPACE` |
| `Pinecone:Videos:EmbeddingModelIdentity` | `PINECONE_VIDEO_MODEL_IDENTITY` |
| `Embedding:ModelPath` | `ONNX_MODEL_PATH` |
| `Embedding:VocabularyPath` | `ONNX_VOCAB_PATH` |
| `Embedding:ModelIdentity` | `EMBEDDING_MODEL_IDENTITY` |

Absent video name/namespace inherit the document name/namespace. **Both target identities remain explicit**, even for a shared target. Defaults for assets are `models/model.onnx` and `models/vocab.txt`, resolved against the supplied content root, not the process working directory. Assets are not committed/downloaded automatically. Dummy/blank API keys are rejected.

Other hierarchical keys (environment form uses `__` in place of `:`):

| Key under `Pinecone:` | Default |
| --- | --- |
| `RequestTimeoutSeconds` | 30 per attempt |
| `BatchSize` | 100 (max 1,000) |
| `MaxPayloadBytes` | 1,900,000 (max 2,000,000) |
| `MaxMetadataBytes` | 40,000 |
| `MaxTopK` | 100 (configurable up to 1,000) |
| `MaxRetries` | 3 |
| `RetryBaseDelayMilliseconds` | 200 |

The strongly typed registration overload accepts `PineconeServiceOptions` and `EmbeddingOptions`, including the trusted `AllowedDocumentIds` authorization scope. `null` means unrestricted, empty means no access. Scope is snapshotted at construction and ANDed with collection/source constraints and caller filters; returned search records are rechecked. Scoped ID deletion uses metadata filters rather than fetching raw vector records or performing unsafe ID-only deletion.

**The auth layer owns authorization.** The configuration overload registers an unrestricted singleton; use it only where callers are authorized for the whole corpus. A scoped service registration is an isolated, immutable scope for that application/container, not request-varying tenant state. Never mutate scope options or share a tenant-specific singleton across tenants. Request-scoped/dynamic authorization needs deliberate orchestration/contract design before exposing cross-tenant endpoints. `DocumentIds` in search options is a narrowing filter, not authorization.

## Storage, search, and transport choices

- The sole production transport is an internal `HttpClient` adapter, pinned with `X-Pinecone-API-Version: 2025-10`. No SDK dependency or second transport is maintained. It supports pre-provisioned dense vector indexes only, not document-schema/sparse indexes. Configured hosts must match control-plane descriptions and use HTTPS `*.pinecone.io`; redirects are disabled.
- Limits checked against Pinecone's [upsert guidance](https://docs.pinecone.io/guides/index-data/upsert-data) and [2025-10 delete API](https://docs.pinecone.io/reference/api/2025-10/data-plane/delete): up to 1,000 records/2 MB per upsert and 40 KB metadata per record; `/vectors/delete` accepts metadata filters. The 40,000-byte metadata ceiling is conservative relative to 40 KiB. We count serialized UTF-8 including escaped Unicode, keys, and lists. This implementation deliberately requires filter deletion support and never simulates it with top-K enumeration.
- Metadata schema v1 allowlists `schema_version`, `model_identity`, `collection`, `source_type`, `chunk_id`, `doc_id`, full `text`, `source`, section ID/title/path, image paths, block IDs, start/end seconds, artifact URL, and video URL. Optional values are omitted; zero seconds survives. Lists are copied/read-only on reconstruction. Unknown fields are ignored; missing/invalid/unsupported content is skipped with diagnostics, never emitted as empty pseudo-content.
- Record IDs are a collection prefix plus SHA256 of the caller's stable chunk ID. Original chunk IDs live in metadata and are verified against the returned record ID. IDs cannot collide between document/video collections sharing a namespace. Migrating old IDs is explicit re-ingestion, not preservation by accident.
- Full content can exceed the text actually embedded: tokenization still truncates at **256 total tokens**, for Python parity. No text/citation truncation, hidden splitting, or alternate storage occurs. Oversize metadata, duplicates, scope violations, and conservative per-record payload bounds are validated before any writes. Actual batches split by both serialized byte size and record count. The conservative float-size preflight can reject a record near an unusually small configured payload ceiling even if its particular embedding might fit.
- Query requests include metadata and explicitly exclude vector values. Service preserves Pinecone order, validates/rechecks content/scope, applies minimum similarity, and assigns one-based final ranks. No over-fetch or feedback reranker is enabled initially; unusable legacy matches may therefore produce fewer than `TopK` usable results. `RerankScore` remains separate/reserved rather than overwriting similarity.
- Only transient network failures, timeouts, 429, and selected 5xx statuses retry. Backoff is bounded/jittered; `Retry-After` is respected. Delays over one minute fail rather than retrying earlier than instructed. Invalid requests, permissions/auth, incompatible targets, and malformed successful JSON do not retry. There is no SDK retry stack. HTTP error bodies/raw exceptions are not surfaced or logged.
- Logs carry operation IDs, latency, counts, batch sizes, and retries, not chunk text, raw queries, vectors, or keys. Meter `CFC.ChatAI.Rag.Pinecone` publishes `cfc.pinecone.invalid_metadata`, `cfc.pinecone.retries`, and `cfc.pinecone.operation.duration`.

## Acknowledgement and recovery

Successful operations acknowledge requests, **not immediate query visibility or prior record existence**. Writes return acknowledged chunk IDs. Both ID-filter deletion and document-filter deletion have unknown affected counts (`null`); acknowledged IDs refer to requested filters, not proof that matching records existed. Empty writes/deletes are no-ops. Repeat upsert/deletion is idempotent for stable inputs, subject to concurrent writes.

A multi-batch operation is not atomic. `ChunkOperationException` exposes completed IDs, unresolved IDs, operation ID, cancellation status, an indeterminate flag, and the underlying domain `FailureCategory` when known. Correct configuration/model/input errors before retrying; do not treat every partial-operation exception as a transient failure. A dispatched failed/timed-out batch is conservatively indeterminate. Retry unresolved IDs with exactly the same content/routing/scope; do not regenerate chunk IDs. Document-filter failures carry `DocumentId` and indeterminate outcome rather than fabricating a chunk list/count. Cancellation before any dispatch is ordinary cancellation; cancellation after dispatch can require reconciliation like a timeout.

Re-ingesting fewer/new IDs does not delete old records automatically. Callers retain the complete previous ID set, upsert the new set, then call `DeleteChunksAsync` for obsolete IDs. Never infer a complete previous set from similarity search. Serialize operations per document in orchestration when needed. Restart-safe jobs, generation isolation, catalog/files, and cleanup transactions remain outside this service.

## Tests

Offline (no credentials/model files required):

```text
dotnet test dotnet/tests/CFC.ChatAI.Rag.Tests/CFC.ChatAI.Rag.Tests.csproj
```

The opt-in tests skip by default:

- **ONNX parity:** set `RUN_ONNX_INTEGRATION=1`, `ONNX_TEST_MODEL_PATH`, and `ONNX_TEST_VOCAB_PATH`. Tests use all committed Python references, threshold cosine >= 0.9999, dimension 384, and finite approximately unit-length outputs. The existing harness still works with friend access and now exits nonzero on parity failure.
- **Live Pinecone:** set `RUN_PINECONE_INTEGRATION=1`, asset paths above, `PINECONE_TEST_API_KEY`, `PINECONE_TEST_INDEX_NAME`, and `PINECONE_TEST_MODEL_IDENTITY`. Use a dedicated disposable pre-provisioned test index, **never production data**. Tests never read production key/index variables, create a unique `cfc-service-test-*` namespace, poll with a bounded deadline, verify full metadata/shared-collection isolation/filter deletion, and clean up through `IPineconeService`. Cleanup must also succeed; interrupted runs may leave test namespaces/records requiring service-driven cleanup.

Offline transport tests verify dimension/metric rejection, API version/wire shape, cached readiness, transient retry limits, timeouts/cancellation, and no auth/input retries. Live distinct-target routing, real model/export rollout, representative video metadata sizing, and production cutover remain deployment checks until suitable test targets/assets are available.

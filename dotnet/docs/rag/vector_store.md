# Python vector store → `CFC.ChatAI.Pinecone`

**Status:** SDK-backed implementation migrated behind `IPineconeService`; live read-only
parity evidence retained. Write operations and stats need an isolated-index verification.

## Public integration

The public module is `dotnet/src/CFC.ChatAI.Pinecone/`, with namespace
`CFC.ChatAI.Pinecone`. Consumers register it once and inject `IPineconeService`:

```csharp
services.AddPineconeInfrastructure(options =>
{
    options.ApiKey = configuration["Pinecone:ApiKey"]!;
    options.IndexName = configuration["Pinecone:IndexName"]!;
    options.Namespace = configuration["Pinecone:Namespace"];
    options.ModelPath = configuration["Embeddings:ModelPath"]!;
    options.VocabularyPath = configuration["Embeddings:VocabularyPath"]!;
});
```

The interface provides text `SearchAsync`, text-record `UpsertAsync`, ID-based `DeleteAsync`,
and normalized `GetIndexStatsAsync`. The Pinecone SDK adapter, embedding implementation,
tokenizer, and vector-level types are internal under `_Internal/`; contracts do not return
Pinecone SDK models. Metadata and filter values use `System.Text.Json.JsonElement` so their JSON
shape is explicit. The ONNX embedding model and service are singleton-scoped through DI, and
DI owns disposal of the model.

`PineconeSearchResult` deliberately includes `QueryVector` as well as ordered semantic matches.
Feedback/query-aware reranking can reuse the exact embedding sent to Pinecone without embedding
again. The vector is exposed as a CFC-owned result value; callers should keep feedback persistence,
reranking policy, context formatting, Supabase hydration, and chat orchestration downstream.

Async methods accept cancellation tokens. The adapter currently wraps the SDK tasks in
cancellation-aware waits and checks cancellation before/after work; cancellation stops waiting at
the service boundary but does not necessarily abort an HTTP request already in progress.

## Preserved behavior

- Uses the official `Pinecone.Client` SDK and configured index/optional namespace; it does not
  provision indexes automatically.
- Query and upsert embed text using the existing all-MiniLM-L6-v2 ONNX model. Upserts batch
  vectors in groups of 100.
- Record IDs and caller metadata remain unchanged; record text is not copied into Pinecone
  metadata. Existing Python/Pinecone records therefore need no re-ingestion.
- Delete remains ID-based and an empty ID collection is a no-op.
- Configuration currently identifies one index per registered service. If the .NET application
  needs separate document/video targets, register deliberately configured service instances;
  routing/lifecycle orchestration is not implicit in this package.

## Verification evidence

The read-only parity check was run against the existing production index. Python's
`VectorStore` recorded vectors and top-5 results for 15 realistic questions
(`tools/make_retrieval_reference.py` → `TestData/retrieval_reference.json`). Replaying the exact
vectors isolates Pinecone query behavior from embedding behavior:

- 15 questions: 10 exact-order passes, 5 tie-only order differences, 0 failures.
- Scores matched to 6 decimal places at every rank. Differences were only Pinecone reordering
  exact-score ties, including one tied item at the top-5 cutoff.
- The text-layer parity test compares the C# ONNX vector against those Python vectors with cosine
  ≥ 0.9999. The prior live text check recorded 12 exact-order passes, 3 tie-only differences, and
  0 failures.

These checks are read-only. Live checks can be run with
`dotnet run --project tools/CFC.ChatAI.Rag.VectorStoreCheck -- --text` from `dotnet/`; run without
`--text` to replay saved query vectors. They require the model assets and Pinecone credentials.

## Implementation notes and remaining verification

- `IPineconeService` is the sole public operational interface. `IVectorStore`,
  `PineconeVectorStore`, `IEmbeddingModel`, and the old text facade are internal seams used by
  tests and diagnostic tools, not application APIs.
- `PineconeIndexStats` normalizes total vector count, dimension, and namespace counts instead of
  exposing the SDK response object.
- Unit tests cover query-vector reuse, upsert mapping, cancellation checks, and legacy parity
  wiring. Existing ONNX embedding parity tests remain in place.
- No production write, delete, index migration, or data migration was performed. Verify writes,
  deletion, metadata/filter mapping, stats normalization, and DI startup against fakes or an isolated
  Pinecone index before enabling those operations in production.
- `delete_by_prefix` remains out of scope (unused in the Python app). Automatic index creation,
  document lifecycle orchestration, feedback persistence/reranking, and prompt-context assembly
  also remain out of this integration.

# app/core/vector_store.py → CFC.ChatAI.Rag/PineconeVectorStore.cs

**Status:** Ported · Query verified (read-only) · Write path untested
**Owner:** Logan Loch
**Last edited:** 10/7/2026

## What it does
Thin wrapper around one Pinecone index (and optional namespace): stores chunk vectors,
searches for the chunks most similar to a query vector, deletes vectors, and reports stats.

## Used by (Python)
- `app/core/rag.py`: `query()` for every chat question
- `app/api/endpoints/ingest.py`: `upsert_vectors()` during ingestion
- `app/api/endpoints/admin/documents.py`: `delete_document()` when a document is deleted
- `app/api/endpoints/visibility.py`, `app/services/chat_service.py`: `get_index_stats()`
- `chat_service.py` creates a second instance for the **video** index

## Methods

| Python | C# | Status | Notes |
|---|---|---|---|
| `query(vector, top_k, include_metadata, metadata_filter)` | `QueryAsync(...)` | Verified | Filter kept for the two-product requirement |
| `upsert_vectors(vectors)` | `UpsertAsync(records)` | Ported, untested | Batched (100/request); Python sends all at once |
| `delete_document(chunk_ids)` | `DeleteAsync(ids)` | Ported, untested | No-op on empty list (same as Python) |
| `get_index_stats()` | `GetIndexStatsAsync()` | Ported, untested | |
| `delete_by_prefix(prefix)` | — | Dropped | Never called; only logged, never deleted |
| lazy index creation | — | Deferred | Production index exists; auto-creating is risky |

## Behavior to preserve
- Index name and namespace come from configuration (`PINECONE_INDEX_NAME`, `PINECONE_NAMESPACE`);
  namespace is optional
- The video index is a separate instance (`PINECONE_VIDEO_INDEX_NAME`)
- Metric: cosine; dimension 384 (must match the embedding model)

## Quirks and bugs found
- `delete_by_prefix` logs but never actually deletes, and nothing calls it
- `upsert_vectors` sends all vectors in one request; large documents could exceed
  Pinecone's request size limit (~2 MB / 1,000 vectors)

## Migration decisions
- **Async methods** (`QueryAsync`, etc.): idiomatic for ASP.NET; Python's are synchronous
- **Batched upserts** of 100 vectors per request
- **No automatic index creation**: create indexes deliberately, not as a side effect
- **`using Vector = Pinecone.Vector;` alias**: `Vector` is ambiguous with
  `System.Numerics.Vector` (visible project-wide through a package's global using)

## Verification in the live app (10/7/2026)

**Setup:** Read-only parity check against the production Pinecone index. Python's own
`VectorStore` recorded the query vector and top-5 results for 15 realistic questions
(`tools/make_retrieval_reference.py` → `TestData/retrieval_reference.json`). The C# check
(`tools/CFC.ChatAI.Rag.VectorStoreCheck`) replays **the exact same query vectors**, which
isolates the vector store from the embedding model. Nothing is written to the index.

**Verdicts:**
- **PASS**: identical chunk IDs in identical order
- **TIE**: identical scores at every rank; differing chunks are only within groups of
  exactly tied scores
- **FAIL**: anything else

**Results:** 15 questions: **10 PASS, 5 TIE, 0 FAIL.**
- Scores matched to 6 decimal places at every rank for every question
- All 5 TIEs were reorderings within exact-tie groups (e.g., four chunks all scoring
  0.587357 for "How do I find an equivalent ingredient?"); one tie at the rank-5 cutoff
  swapped which equally scored chunk was included
- Pinecone reorders exact ties even between identical queries (observed in the embedding
  bridge test), so these differences are not caused by the port

**Conclusion:** `QueryAsync` behaves identically to Python's `query()`. The query path,
which every chat question uses, is verified. Write operations remain untested.

## Findings outside this component
- **Duplicate chunks in the index**

## Text layer: TextVectorStore (10/11/2026)

`PineconeVectorStore` takes vectors, the same as Python's `VectorStore`. `TextVectorStore` is a thin
layer on top that takes **text**: it embeds with `IEmbeddingModel` (the ONNX model), then calls the
vector store. Python has no single class for this. It does the same two steps inline:

| Python (inline) | C# |
|---|---|
| `rag.py` `retrieve_context()`: `encode_query(query)` → `vector_store.query(...)` | `SearchAsync(query, topK, includeMetadata, filter)` |
| `ingest.py` `_prepare_vectors()`: `encode(texts)` → `upsert_vectors(...)` | `UpsertAsync(IReadOnlyList<TextRecord>)` |
| `delete_document(ids)` | `DeleteAsync(ids)` (pass-through) |

- **Wire format unchanged:** same record IDs, metadata passed through as-is, text not copied into
  metadata. Works against the existing production index with no re-ingest.
- `SearchAsync` returns the **query vector** with the matches, because the feedback re-ranking
  (`FeedbackService.get_query_aware_scores`) reuses it.
- `UpsertAsync` embeds all texts in **one batch**, as Python does; the vector store still sends
  them to Pinecone 100 at a time.
- `IEmbeddingModel` and `IVectorStore` were added (implemented by `OnnxEmbeddingModel` and
  `PineconeVectorStore`) so the layer can be unit-tested with fakes. Their public methods are unchanged.
- `PineconeVectorStore` stays public for vector-level work (`TextVectorStore.Vectors`).

**Tests:**
- `TextVectorStoreTests`: wiring tests with fakes (no model, no network)
- `TextVectorStoreParityTests`: the vector sent to Pinecone for each of the 15 reference questions
  matches the vector Python queried with (cosine ≥ 0.9999). Offline; uses the real ONNX model.
- Live: `dotnet run --project tools/CFC.ChatAI.Rag.VectorStoreCheck -- --text` runs the 15 questions
  as text through `TextVectorStore` against the production index (read-only).
  **Result (10/11/2026): 15 questions: 12 PASS, 3 TIE, 0 FAIL.** Scores within ±0.000001 of
  Python's; all TIEs are reorderings within exact-tie groups (the same behavior as the vector replay).

## Comparison with PineconeService (branch migration/first-migration-chunk)

Brayden's `IPineconeService` is also text-in (whole chunks and query text). It is a bigger
redesign, not a port, and **it is not compatible with the current production index**:

| | PineconeVectorStore + TextVectorStore | PineconeService |
|---|---|---|
| Pinecone access | Official `Pinecone.Client` SDK | Hand-written HTTP transport |
| Record IDs | Python's chunk IDs, unchanged | `document:<sha256(chunk_id)>`; records whose ID doesn't match are skipped |
| Metadata | Python's minimal metadata; text stays in Supabase | New schema with full text plus `schema_version`, `model_identity`, `collection` |
| Search filter | Caller's filter only | Always requires `schema_version`, `model_identity`, `collection` |
| Production index today | Verified (15 questions: 10 PASS, 5 TIE, 0 FAIL) | Would return no results: existing records lack those fields. Its own doc says it needs a new index and a full re-ingest |
| Hardening | Basic | Payload-size budgets, partial-write errors, retries, document-ID scope, health check, cancellation |

Worth adopting from PineconeService later: separate document/video target configuration, a health
check that actually hits the index, cancellation tokens, and its finding that Python writes video
chunks to the document index while video retrieval reads the video index (needs confirming).

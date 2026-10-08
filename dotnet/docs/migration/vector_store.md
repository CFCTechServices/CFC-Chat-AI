# app/core/vector_store.py -> CFC.ChatAI.Rag/PineconeVectorStore.cs

**Status** In Progress 
**Owner** Logan Loch
**Last Edit** 10/7/2026

## What it does
Thin wrapper around one Pinecone index (and optimal namespace): stores chunk vectors, searches for the most similar chunks to query a vector, deletes vectors, and reports stats.

## Used by (Python)
- `app/core/rag.py`: `query()` for every chat question
- `app/api/endpoints/ingest.py`: `upsert_vectors()` during ingestion
- `app/api/endpoints/admin/documents.py`: `delete_document()` when a document is deleted
- `app/api/endpoints/visibility.py`, `app/services/chat_services.py`: `get_index_stats()`
- `chat_service.py` creates a second isnstance for the **video** index

## Methods
| Python | C# | Status | Notes |
|---|---|---|---|
| `query(vector, top_k, include_metadata, metadata_filter)` | `QueryAsync(...)` | | Filter kept for the two-product requirement |
| `upsert_vectors(vectors)` | `UpsertAsync(records)` | | Batched (100/request); Python sends all at once |
| `delete_document(chunk_ids)` | `DeleteAsync(ids)` | | No-op on empty list (same as Python) |
| `get_index_stats()` | `GetIndexStatsAsync()` | | |
| `delete_by_prefix(prefix)` | — | Dropped | Never called; only logged, never deleted |
| lazy index creation | — | Deferred | Production index exists; auto-creating is risky |

## Behavior to preserve
- Index name and namespace come from configuration (`PINECONE_INDEX_NAME`, `PINE_NAMESPACE`); namespace is optional
- video index is a seperate instance (`PINECONE_VIDEO_INDEX_NAME`)
- metric: cosine; dimension 384 (must match the embedding model)

## Quirks and bugs found
- `delete_by_prefix` logs but never actually deletes, nothing calls it
- `upsert_vectors` sends all vectors in one request; large documents could exceed Pinecone's request size limit ~2MB/1,000 vectors
## Migration Decisions
- **Async methods** (`QueryAsync`, etc.): idiomatic for ASP.NET; Python's are synchronous
- **Batched upserts** of 100 vectors per request
- **No automatic index creation**: create indexes deliberately, not as a side effect

## Verification in the live app

**Setup**

**Results**

**Conclusion**

## Findings outside this component

## Setup

## How it's tested
- Read-only parity check: same query vector -> same chunk IDs and scores as Python 
- Write operations tested only in a seperate namespace, then cleaned up

## Open Items
- [] Read-only parity test against the production index
- [] Write-path test (upsert/delete) in a scratch namespace 
- [] Product filter design (two-product requirement)
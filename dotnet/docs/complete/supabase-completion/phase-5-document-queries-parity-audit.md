# Supabase completion — Phase 5: document queries and parity audit

**Status: document-query contracts/adapters and adapter tests are implemented. Source-level Storage parity has been audited. Phase 5 is not fully closed for rollout:** no target Supabase project/object inventory was available, self-service profile writes remain blocked by the supplied privilege evidence, and the active .NET detailed-health endpoint still makes a direct PostgREST request outside `CFC.ChatAI.Supabase`.

## Document query delivery

Extended `IChunkRepository` with:

- `GetDocumentMetadataAsync(documentId)`, returning `null` for a document with no chunk rows and otherwise returning `DocumentId`, `Source`, and `SourceType`.
- `ListDocumentSummariesAsync(page)`, returning `DocumentId`, `Source`, `SourceType`, and `ChunkCount` in stable source-then-document-ID order. The ordering preserves the current Python listing's source sort while adding a deterministic tie-breaker.

The adapter scans `document_chunks` using sequential PostgREST pages of at most 1,000 rows, ordered by document ID and chunk ID, then aggregates and pages the document summaries. A per-document metadata lookup is likewise paged so it can detect conflicting non-empty source/source-type values; such a conflict raises `DocumentMetadataInconsistencyException` rather than arbitrarily choosing one row. These operations are database-only and use the existing service-role path. They do not read vectors or coordinate document lifecycle work.

This preserves the current Python aggregation behavior without an implicit view/RPC/schema migration, but it has an important cost: each summary request currently scans all chunk rows and aggregates them in memory before paging the summary result. The operation is cancellation-aware and each HTTP request is bounded, but total scan size is not capped and the multi-request scan is not a snapshot/transaction. If the corpus makes this expensive or concurrent updates make a consistent snapshot necessary, a separately reviewed view/RPC is appropriate.

## Storage parity audit

### Capabilities present in the adapter

- `StorageArea.Documents` and `StorageArea.Videos` map to independently configured bucket names, defaulting to `cfc-docs` and `cfc-videos`.
- Upload supports overwrite through `x-upsert` and does not take ownership of the caller's stream.
- Download accepts a validated relative `ObjectKey`; the returned stream owns the HTTP response and must be disposed by its caller.
- Listing sends prefix, limit, offset, and name-ordering parameters and returns direct child entries plus a continuation indicator. It does **not** recursively walk folders; callers must traverse folder entries and all pages.
- Delete is object-only and batched; it does not delete chunk rows or vectors.
- Signed URLs take an explicit positive lifetime, now capped at one hour. The current document-download flow asks for 300 seconds. Relative Storage API URL responses are resolved beneath `/storage/v1`, and absolute responses are preserved.
- Public URL construction is not proof that a bucket/object is public or exists.

### Object paths evidenced in checked-in code

These are source-code examples, not evidence that the objects exist in a deployed bucket:

| Area | Paths observed in application code |
|---|---|
| Documents | `docs/{doc_id}/original/{filename}`, `docs/{doc_id}/sections/{section_id}.json`, and `docs/{doc_id}/images/{filename}` |
| Videos — active upload route | `videos/{slug}/original/{slug}{extension}`, `videos/{slug}/transcripts/{slug}.{txt,srt,vtt}`, and `videos/{slug}/summary/{slug}.md` |
| Videos — older repository layout | `videos/original/{slug}/{filename}`, `videos/transcript/{slug}.{format}`, and `videos/summaries/{slug}.{extension}` |
| Images — compatibility path | The image handler also recognizes legacy `images/{filename}` keys; these can be addressed through the documents bucket, but deployed presence is unknown. |

The current admin download flow looks under `videos/{doc_id}/original` for video originals and `docs/{doc_id}/original` for document originals. Upload, legacy helper, and cleanup paths are not completely uniform (for example `summary` vs `summaries`, and the older `videos/original/...` layout). Do not normalize, delete, or claim parity across these layouts until representative live object keys and cleanup expectations are checked.

### Live Storage checks still required

No Supabase credentials, target-project identity, bucket listing, or sanitized object manifest was available. Therefore this repository audit cannot verify that the configured bucket names match deployment, that either bucket is private/public as expected, that service-role policies permit the operations, or that object listings/downloads work against the deployed Storage API version. Before rollout, test real representative document/video/image objects, overwrite behavior, nested-folder pagination/traversal, deletion, and a 300-second signed URL (including expiry). No real object or signed URL was fetched in this phase.

## Active endpoint coverage matrix

This matrix records **Supabase persistence support needed by each active API route**, not whether the route itself has already been ported to .NET. “Covered” means a focused contract/adapter is available for its Supabase operation; host/application authorization and non-Supabase work remain above this module. The 42 active routes below agree with the repository migration inventory. Unmounted transcript routes are excluded.

| Active route | Supabase operation/status |
|---|---|
| `GET /api/health` | **Intentionally non-Supabase** — process health only. |
| `GET /api/health/detailed` | **Boundary gap** — the current .NET host sends a direct `GET /rest/v1/profiles` request in `dotnet/src/CFC.ChatAI.Api/Endpoints/Health/HealthEndpoints.cs`; this is a Supabase call outside this package and must be moved behind a focused Supabase health capability before the phase boundary criterion is met. Other checks in this endpoint (Pinecone/AI/model configuration) are host/provider concerns. |
| `GET /api/auth/config` | **Intentionally non-Supabase** — host configuration endpoint; only public auth URL/anon configuration belongs here. |
| `GET /api/visibility/vector-store` | **Intentionally non-Supabase** — vector-store statistics, not Supabase persistence. |
| `POST /api/auth/forgot-password` | **Covered** — `IIdentityGateway`; auth/provider response and enumeration behavior still require API-level verification. |
| `POST /api/auth/check-email` | **Covered** — `IInvitationRepository` plus profile email lookup through `IAdminProfileRepository`. Expiry/status decisions remain application logic. |
| `GET /api/profile/me` | **Covered** — user-token `IUserProfileRepository`; deployed RLS behavior remains to be verified. |
| `PATCH /api/profile/me` | **Blocked by supplied schema/privilege evidence** — adapter allow-listing exists, but the supplied effective ACL export grants authenticated INSERT/UPDATE on privileged profile columns and policies constrain only row ID. Do not enable self-service writes until database-side restrictions and direct negative tests are verified. Export project identity remains unconfirmed. |
| `POST /api/profile/complete` | **Blocked by the same supplied profile-write evidence** — shares self-profile update persistence. |
| `POST /api/admin/invite` | **Covered** — invitation writes/lookups and profile email lookup; partial unique-index/default/permissions still require deployment verification. |
| `GET /api/admin/invitations/status/{email}` | **Covered** — latest invitation lookup; status/expiry policy is application-owned. |
| `GET /api/admin/users` | **Covered** — paged `IAdminProfileRepository`. |
| `POST /api/admin/change-role` | **Covered** — privileged profile role update; role authorization stays in the application. |
| `POST /api/admin/users/{user_id}/deactivate` | **Covered** — privileged profile state update plus existing Auth admin operations. |
| `POST /api/admin/users/{user_id}/reactivate` | **Covered** — privileged profile state update plus existing Auth admin operations. |
| `DELETE /api/admin/users/{user_id}` | **Components covered; lifecycle blocked pending schema verification** — invitation-by-email delete and Auth user deletion exist, but profile/auth foreign-key cascade and deletion side effects have not been verified. Do not claim complete account deletion until confirmed. |
| `GET /api/admin/settings` | **Intentionally non-Supabase** — local settings. |
| `PATCH /api/admin/settings` | **Intentionally non-Supabase** — local settings. |
| `GET /api/admin/ingestion/stats` | **Intentionally non-Supabase** — local ingestion/filesystem status. |
| `POST /api/chat/search` | **Covered for Supabase hydration/scoring** — `IChunkRepository` and feedback reads; vector search/embedding remain outside this package. |
| `POST /api/chat/ask` | **Covered for Supabase hydration/scoring** — `IChunkRepository` and feedback reads; retrieval/model orchestration remains above this package. |
| `POST /api/chat/ask/video` | **Covered for Supabase hydration/scoring** — video metadata is part of chunk DTOs; vector/model work remains outside. |
| `POST /api/chat/recommendations` | **Covered for Supabase hydration/scoring** — chunk and feedback persistence only; recommendation workflow remains above. |
| `GET /api/chat/sessions` | **Covered** — user-token conversation listing, including detail projection. Deployed RLS remains unverified. |
| `POST /api/chat/sessions` | **Covered** — user-token session creation; deployed insert policy remains unverified. |
| `GET /api/chat/sessions/{session_id}` | **Covered** — owner-scoped session/history reads; message policies remain unverified. |
| `DELETE /api/chat/sessions/{session_id}` | **Covered with partial-failure semantics** — message and session deletes are separate requests, not a transaction. |
| `PATCH /api/chat/sessions/{session_id}` | **Covered** — owner-scoped title update; deployed update policy remains unverified. |
| `POST /api/chat/message` | **Persistence covered** — conversation session/message/context operations exist. Chat generation stays outside. This .NET contract uses caller-token access; Python currently uses service-role calls for message workflow, so owner approval and RLS/integration verification are required before treating it as behaviorally interchangeable. |
| `POST /api/chat/feedback` | **Covered** — feedback submit RPC and feedback context capabilities; ownership/rating policy remains in the application. RPC and grants require live verification. |
| `GET /api/chat/feedback` | **Covered** — bounded user-filtered non-null rating lookup; ownership validation remains in the conversation/application workflow. |
| `POST /api/files/upload` | **Covered for Supabase persistence** — Storage + chunk upsert; parsing, embeddings, vectors, and orchestration intentionally stay above this package. |
| `POST /api/files/bulk` | **Covered for Supabase persistence** — same focused Storage/chunk operations; bulk partial failures are workflow-owned. |
| `POST /api/ingest/document` | **Covered for Supabase persistence** — chunk writes and object operations only; ingestion orchestration stays above. |
| `POST /api/ingest/bulk` | **Covered for Supabase persistence** — same boundary; no cross-provider transaction is claimed. |
| `GET /api/admin/documents` | **Covered** — new document summaries with source/type/chunk count; full-table scan cost noted above. |
| `GET /api/admin/documents/{doc_id}/download` | **Covered** — document metadata lookup, bucket/prefix listing, download URL signing; actual path layouts and bucket policies remain unverified. |
| `DELETE /api/admin/documents/{doc_id}` | **Supabase components covered** — document chunk IDs/deletion and Storage operations exist. Pinecone/vector coordination and partial-failure recovery belong to the application workflow. |
| `PUT /api/admin/documents/{doc_id}/replace` | **Supabase components covered** — metadata/chunk and Storage operations exist. Purge/re-ingestion/vector coordination and recovery belong above this package. |
| `POST /api/videos/upload` | **Covered for Supabase persistence** — video-area object operations and chunk writes; transcription, embeddings, and vector indexing are intentionally outside. |
| `GET /api/chat/content/images/{path:path}` | **Covered for Supabase reads** — validated documents-area object download; local fallback, MIME/cache headers, and endpoint path policy remain host concerns. |
| `GET /content/images/{path:path}` | **Covered for Supabase reads** — compatibility alias may use the same object contract; it must share the primary handler's path validation. |

## Boundary and blocker findings

1. **Direct Supabase call remains in API host health diagnostics.** Repository search found a raw PostgREST request in `HealthEndpoints.CheckSupabaseAsync`. Apart from this host-level probe, `CFC.ChatAI.Supabase` contains the Supabase REST/Auth/Storage implementation. The detailed-health route is a Phase 5 boundary gap; it should use a small explicit health-check contract rather than constructing Supabase HTTP calls in the API project.
2. **Profile self-write is not cleared for production.** The supplied profile privilege export and self-row policy evidence identify all-column write access as a blocker. The export's project identity and any database-side remediation/direct tests are not established here.
3. **User/service scope remains an integration decision.** Several Python chat paths use service-role access with explicit application ownership checks, while the current conversation repository is caller-token/RLS scoped. Do not silently treat that change as verified equivalent.
4. **Database/Storage facts remain unverified.** The local `schema.sql` is expressly contextual, and policy/privilege exports do not cover all tables, RPCs, buckets, FK actions, indexes, or actual objects.

## Tests and architecture checks

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **24 passed, 0 failed**. New adapter coverage verifies document summary aggregation/paging/order, missing document metadata, inconsistent source detection, videos bucket listing/pagination, relative signed-URL resolution, and signed-URL lifetime bounds. Existing Storage coverage also checks upload overwrite configuration and caller stream ownership.

Source inspection found no Pinecone SDK, API/application project reference, or provider-specific runtime call path in `CFC.ChatAI.Supabase`; embedding values are accepted only as supplied data for database persistence/scoring. `CFC.ChatAI.Api` does still perform the direct Supabase health call noted above. No live Supabase integration, real Storage object traversal, signed URL expiry, or deployment-target schema verification was performed.

## Phase 5 exit assessment

- [x] Document source metadata and document summaries added to chunk persistence.
- [x] Per-request HTTP work is paged and cancellation-aware; document-list scan cost and lack of snapshot semantics are documented.
- [x] Storage bucket/path behavior audited against repository code; overwrite, download ownership, listing pagination, recursive traversal responsibility, and signed URL lifetime documented.
- [x] Active route-to-Supabase capability matrix recorded for all 42 registered routes.
- [ ] Verify actual configured buckets and representative document/video/image object paths, nested folder pages, object ACLs, overwrite, and signed URL expiry in a controlled Supabase project.
- [ ] Move the detailed-health Supabase probe into `CFC.ChatAI.Supabase` so no active endpoint makes a direct PostgREST/Auth/Storage call outside the package.
- [ ] Verify supplied profile exports' project identity and database-side self-write restriction with direct authenticated negative tests.
- [ ] Resolve/verify user-vs-service scope for chat paths, relevant RLS/grants, FK/cascade behavior, invitation constraint/default, and other database prerequisites before production use.

**Phase 5 implementation work is delivered, but Phase 5's full exit criteria are not met.** The remaining items are explicitly blocked on an application boundary fix and target-project/database/Storage evidence, not assumed successful from unit tests.

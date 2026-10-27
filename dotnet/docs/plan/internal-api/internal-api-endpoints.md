# Internal API endpoint migration plan

## Scope and baseline

Plan for the **mounted, currently useful HTTP API** in `main.py` and `app/api/endpoints/`, not an implementation-status claim. `dotnet/src/CFC.ChatAI.Api/Program.cs` currently maps **only** `/api/health` and `/api/health/detailed`; all other routes below need API registration, request/response DTOs and tests. Keep the Python route paths and frontend-facing JSON shapes (including existing camelCase `auth/config` keys, and snake_case elsewhere) unless a versioned contract change is agreed. Compare against Python endpoint tests and real client calls before cutover. `dotnet/docs/pinecone-supabase-both-or-none.md` is the provider inventory; component contracts live in `dotnet/src/CFC.ChatAI.*`. “Supabase” below includes Auth, database and Storage.

Out of scope for now, per `dotnet/high-level.md`: `POST /api/chat/search`, `/ask`, `/ask/video`, `/recommendations`, `POST /api/files/bulk`, and `POST /api/ingest/bulk`. `POST /api/ingest/document` is hidden and not used by the UI: implement **only if** a manual/local-file ingestion workflow is still needed, otherwise call `IDocumentHandler` directly from an operator tool. `app/api/endpoints/transcripts.py` is **not mounted** in `main.py` (including `/api/videos`, `/api/videos/{slug}/transcript`, and `/api/debug/where`); do not port it just because the file exists. Static frontend fallback is a separate hosting concern. Image routes are included as compatibility paths even though they are not the main JSON API.

## Shared API work (do this first)

- Add project references/DI registrations to the API for the needed existing components: Supabase repositories/gateways, `IPineconeService`, `IFeedbackService`, `IChatService`, `IDocumentHandler`. API owns orchestration, HTTP DTOs and authorization; never make Supabase or Pinecone depend on each other. DocumentHandler owns cross-provider ingest/delete; ChatService owns persisted message/RAG; Feedback owns reranking. DocumentProcessor and VideoProcessor are used **through DocumentHandler**, not directly by routes.
- Add bearer authentication using `IIdentityGateway.GetUserAsync` and a current-user policy backed by the current profile (role/status/deleted state). Require admin policy on all `/api/admin/*` routes. Do not accept user ID/role from request bodies as authority; restrict inactive/deleted users. Decide and test caller-token/RLS versus service-role access for conversation and feedback flows before wiring them: the .NET conversation repository takes `userId` **and** `accessToken`, whereas some Python chat paths use service-role calls. Never expose service credentials. Keep public routes public only intentionally (health, auth/config, forgot-password, check-email; assess vector stats and image disclosure).
- Define explicit endpoint DTOs, multipart/form limits, snake_case serialization exceptions, validated IDs/emails/filenames/slug/path, cancellation, safe error mapping (400/401/403/404/409/429/5xx), timeouts and rate limits; do not forward provider exceptions, token data, PII or signed URLs to logs. Choose pagination policy where Python returns whole lists but repositories expose bounded pages. Match frontend error/success bodies where needed; record deliberate divergences. Cross-provider writes are not transactional: report incomplete work and provide repair/retry procedures instead of silently returning success.
- Test each route with fake components for auth, ownership, response shape and failure mapping, plus disposable non-production Supabase/Storage/Pinecone integration tests for permissions, bucket keys, RPCs and partial failures. See `dotnet/docs/plan/supabase-completion-remaining-plan.md` for outstanding database/RLS/security gates; these must not be assumed complete.

## Routes (component use → API work / component changes)

### Health, configuration and visibility

| Route | Components and plan |
|---|---|
| `GET /api/health` | Already mapped; host-only response; retain version/message shape, no provider call. |
| `GET /api/health/detailed` | Already mapped, but currently probes Supabase directly via HTTP and Pinecone control-plane HTTP in `HealthEndpoints.cs`. Move Supabase probe behind a focused Supabase health contract (or explicitly approve the exception); use `IPineconeService.GetIndexStatsAsync` if index-data-plane health is desired, **not** as a silent replacement for the current control-plane probe. Keep Azure OpenAI config/model-file checks host-owned; preserve degraded/not-configured semantics and bounded timeouts. |
| `GET /api/auth/config` | Host config only: expose **public anon key** and URL in legacy `supabaseUrl`/`supabaseKey` shape; never service-role key. No component change. |
| `GET /api/visibility/vector-store` | `IPineconeService.GetIndexStatsAsync`; map index name, total vectors, dimension, fullness and namespaces into Python's response shape. Check whether this unauthenticated stats endpoint should remain public; likely no provider change. |

### Auth, profile, invitations and users

| Route | Components and plan |
|---|---|
| `POST /api/auth/forgot-password` | `IIdentityGateway.RequestPasswordRecoveryAsync`, configured frontend redirect. Preserve non-enumerating generic response and special 429 handling; add per-IP/email rate limiting. Verify anon-vs-service-key recovery behavior against the target Auth project before parity claim. |
| `POST /api/auth/check-email` | `IInvitationRepository.GetUnregisteredByEmailAsync`, injected UTC clock; require unregistered, unexpired invitation, keep public eligibility response. Decide whether existing profile/Auth account check is required (Python only checks invite); avoid leaking more account detail. No repository change expected. |
| `GET /api/profile/me` | Current user + `IUserProfileRepository.GetCurrentAsync` under caller token/RLS; 404 if missing. Map profile fields including role/status. |
| `PATCH /api/profile/me` and `POST /api/profile/complete` | Share API handler validating at least one allowed field; `UpdateCurrentAsync` accepts only full_name/avatar_url. **Do not enable either write** until profile database grants/RLS/creation path are remediated and directly tested per `dotnet/docs/plan/supabase-profile-self-service-security.md`; DTO allow-list is not sufficient. |
| `POST /api/admin/invite` | Admin policy; `IAdminProfileRepository.GetByEmailAsync` + invitation lookup/delete/insert. Implement existing-account and pending-invite checks, expiry/re-invite using UTC clock, typed `InvitationConflictException` → 409. Delete-then-insert is non-atomic: surface failure and reconcile, do not promise an email was sent (Python invitation is a whitelist entry). |
| `GET /api/admin/invitations/status/{email}` | Admin + `IInvitationRepository.GetLatestByEmailAsync`; map `none`/`active`/`expired`/`registered`, expiry date. No component change. |
| `GET /api/admin/users` | Admin + `IAdminProfileRepository.ListAsync`; map role/status/deletion fields, counts/filters and `total` as expected by clients. Repository is paged: aggregate deliberately or add a separate count/filter contract if needed, not an unbounded query. |
| `POST /api/admin/change-role` | Admin + `GetByIdAsync`/`UpdateRoleAsync`; application enforces allowed roles/role transitions, target superuser protection, self-action policy. Repositories intentionally do not enforce roles. |
| `POST /api/admin/users/{user_id}/deactivate` | Admin + profile lookup/state update and `IAdminIdentityGateway` (lookup, ban, revoke sessions); block self/superuser, validate reason, retain response shape. Define and test best-effort Auth failure policy vs profile update order and partial-failure reporting. |
| `POST /api/admin/users/{user_id}/reactivate` | Admin + profile lookup/state update and identity unban; block invalid transitions; preserve deletion metadata semantics. Explicitly handle partial Auth/profile failure. |
| `DELETE /api/admin/users/{user_id}` | Admin + profile/identity lookups, `IInvitationRepository.DeleteByEmailAsync` and `IAdminIdentityGateway.DeleteUserAsync`; prohibit self/superuser, confirm cascade/profile and invitation behavior in actual database, avoid falsely reporting full deletion on partial failure. |

### Local admin state

| Route | Components and plan |
|---|---|
| `GET /api/admin/settings` | Admin + host-owned settings store (Python uses `DATA_DIR/admin_settings.json`), not Supabase. Add .NET typed, validated settings DTO and local durable file implementation with safe defaults/corruption handling; decide whether multi-instance deployment requires shared persistence. |
| `PATCH /api/admin/settings` | Same store; allow only provided supported fields, reject empty patch, use atomic write/locking to avoid concurrent/corrupt JSON. No existing component owns this state. |
| `GET /api/admin/ingestion/stats` | Admin + local documents/processed-directory scan as in Python, **not** Pinecone/Supabase ingestion stats. Add configured path/extension mapping and read-only filesystem reporting in API; if local staging disappears in .NET, explicitly redefine/retire this metric rather than claim it represents cloud ingestion. |

### Sessions, messages and feedback

| Route | Components and plan |
|---|---|
| `GET /api/chat/sessions` | User + `IConversationRepository.ListSessionsAsync(userId, token, page, includeDetails: detail)`; preserve `?detail=true` message_count/last_message preview. Python returns an array; repository pages: choose bounded aggregation or agree on pagination change. |
| `POST /api/chat/sessions` | User + `CreateSessionAsync`; default title `New Chat`, map returned session and failure; never trust request user_id. |
| `GET /api/chat/sessions/{session_id}` | User + `GetSessionAsync` ownership check then `GetHistoryAsync` chronological messages with metadata (citations/images); 404 on missing/not-owned. |
| `PATCH /api/chat/sessions/{session_id}` | User + `UpdateSessionTitleAsync`; validate title and 404 missing/not-owned. |
| `DELETE /api/chat/sessions/{session_id}` | User + `DeleteSessionAsync`, return `{success:true}` only on completion. Map `SessionDeletionPartialFailureException` to an actionable server error/reconciliation path; verify FK/RLS on target DB. |
| `POST /api/chat/message` | User + `IChatService.SendMessageAsync`; enforce owned session, nonempty content, map assistant id/role/content/citations/relevant_images/created_at. ChatService already owns user/assistant persistence, history, `IFeedbackService` retrieval/reranking, `IPineconeService`, Supabase chunks and Azure OpenAI; don't duplicate in API. Map `ChatSessionNotFoundException` to 404, test failure after user message was saved, citation/image metadata and LLM degradation. |
| `POST /api/chat/feedback` | User + `IConversationRepository.GetFeedbackMessageContextAsync` for assistant-message ownership/session match; validate rating -1/1/null; `IFeedbackRepository.SubmitMessageFeedbackAsync` for atomic vote/aggregate. **Additional work:** host/application background job for query-aware event replacement (delete old, embed the query with same embedding model as retrieval, record new cited-chunk events) when feedback enabled; existing Feedback service only reranks, and its query vector from search is not a stand-alone embedding API. Add a focused embedding capability or orchestrator contract, never fake this with a Pinecone search. Decide retry/ordering semantics; events are not atomically replaced. Preserve `{success,score}` response. |
| `GET /api/chat/feedback?session_id=...` | User + session ownership/history IDs via `IConversationRepository`, then `IFeedbackRepository.GetUserScoresForMessagesAsync`; return `{feedback:{message_id:score}}`, exclude null votes. Handle large sessions by batching/page policy. |

### Content ingestion, management and serving

| Route | Components and plan |
|---|---|
| `POST /api/files/upload` | Multipart input → `IDocumentHandler.IngestAsync` for supported document/video extensions; DocumentHandler delegates document/video extraction, chunk DB/storage and Pinecone vectors. Add size/type/filename validation, admin/upload authorization decision (Python upload is public), temporary-file cleanup, backpressure. Map Python `{message,supabase,ingestion}` fields or agree on new response; handler currently provides single ingest result, not Python's best-effort separate Storage mirror. Confirm file-size/bucket limits and whether original storage failure should be fatal or reported separately. |
| `POST /api/videos/upload` | Multipart slug/file/model/language → `IDocumentHandler.IngestAsync` → `IVideoProcessor` (Azure Speech/FFmpeg, **not** Python Whisper). Validate slug, extension, size; decide supported model/language flags (current .NET processor does not implement arbitrary Whisper model choice). **Component gap:** Python returns original-video URL, TXT/SRT/VTT transcript URLs and markdown summary; inspect current VideoProcessor/DocumentHandler outputs and add artifact generation/storage/response contracts if those are still consumed. Verify bucket/key and public/signed URL policy; do not invent parity. In Python this is public; require an explicit authorization decision. |
| `GET /api/admin/documents` | Admin + `IChunkRepository.ListDocumentSummariesAsync`; map `{documents,total}` and source/type/count. Repository aggregates paged rows in memory; measure large-corpus cost and add a count/view contract if needed. |
| `GET /api/admin/documents/{doc_id}/download` | Admin + `IChunkRepository.GetDocumentMetadataAsync`, then `IObjectStorage.ListAsync` original prefix and `CreateSignedUrlAsync` (5 minutes), map `{url,filename}`. Handle document vs video bucket/key and absent originals → 404; original may be missing even when chunks exist. |
| `DELETE /api/admin/documents/{doc_id}` | Admin + `IDocumentHandler.DeleteAsync` (Pinecone IDs, Supabase chunks/storage). Decide absent-id response; **do not copy Python's swallow-errors-and-return-success purge**. Log/report partial failures and support retry; verify cleanup of image/video artifacts. |
| `PUT /api/admin/documents/{doc_id}/replace` | Admin + metadata lookup + validated multipart + `IDocumentHandler.DeleteAsync` then `IngestAsync`. **Component/API gap:** define document ID preservation, original filename/source mapping, video handling, same-key overwrite, and response `{success,message,ingestion}`. Not atomic; ensure failure after delete is surfaced and recoverable, preferably stage new content before destructive purge if policy allows. Never call one HTTP endpoint from another. |
| `GET /api/chat/content/images/{path:path}` and `GET /content/images/{path:path}` | Same image-serving handler/compatibility alias; `IObjectStorage.DownloadAsync` (and deliberate local fallback if legacy data needs it). Validate/decode path safely, determine Storage area/key and content type, stream with correct disposal/cache policy; choose access control for private images. No vector access. |
| `POST /api/ingest/document` (optional) | If retained as an operator-only route, resolve a file **under configured local staging directory** and call `IDocumentHandler.IngestAsync`; block traversal, authenticate/admin-gate it, map `IngestResponse`. Do not reintroduce duplicate ingestion logic or expose local filesystem paths publicly. |

## Rollout order / acceptance

1. Lock client contracts and public-vs-protected route policy; wire auth/DI, health boundary and simple reads/config. Resolve target Supabase schema/RLS/Storage and profile write gate before any self-profile writes.
2. Ship auth/profile/admin/session routes with ownership and role tests; prove user-scoped requests cannot access other users even when service-role operations exist elsewhere.
3. Wire chat/message and feedback vote plus background events; test retrieval/citations/images, empty/missing sessions, LLM errors and partial persistence.
4. Wire files/videos, admin document CRUD/replace/download, image aliases; exercise provider outages between each write and recovery, storage limits and original-download parity.
5. Run route-level Python-vs-.NET contract tests for every retained endpoint and client UI smoke tests; migrate routing only after security, data-shape and failure behavior have explicit sign-off. Omitted routes remain on Python or are removed from routing deliberately; do not add stubs returning success.

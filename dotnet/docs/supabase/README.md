# C# Supabase interfaces

This page documents the Supabase contracts and adapters introduced for the .NET migration. It describes this integration only; the repository-wide setup and the existing Python application are documented elsewhere.

## Organization and dependency direction

```text
CFC.ChatAI.Supabase           Supabase-specific contracts, DTOs, HTTP adapters, and public DI registration
            ^
            |
Application workflows        inject only the capabilities they use
```

The contracts and models in `CFC.ChatAI.Supabase` describe this integration, rather than provider-neutral APIs. Supabase REST/Auth/Storage wire models, URL construction, API keys, and transport details remain internal. Adapter classes are `internal`; applications consume the narrow capabilities exposed by the Supabase package and register them through `AddSupabaseInfrastructure`.

### Public contract map

| Contract | Responsibility |
|---|---|
| `IIdentityGateway` | Validate a bearer token and request password recovery |
| `IAdminIdentityGateway` | Privileged user lookup, ban/unban, global session revocation, and identity deletion |
| `IUserProfileRepository` / `IAdminProfileRepository` | User-token self-profile reads/allow-listed updates and privileged profile administration/email lookup |
| `IInvitationRepository` | Privileged invitation lookups, insert with returned DB fields, and deletes |
| `IConversationRepository` | Caller-token session/message list, create, read, update, delete, history/context, and feedback-context reads |
| `IChunkRepository` | Upsert chunks, hydrate rows by IDs, list document chunk IDs by page, and delete by IDs |
| `IFeedbackRepository` | Atomic feedback submission RPC, current-user message ratings, global/query-aware scores, and query-event persistence |
| `IObjectStorage` | Upload/download/list/delete objects and create signed/public URLs |

Supabase-facing models and contracts are organized by concern under `Identity/`, `Conversations/`, `Chunks/`, `Feedback/`, `Storage/`, and `Common/` in `dotnet/src/CFC.ChatAI.Supabase`. Database operations are grouped by behavior rather than mechanically exposing one repository per table.

Authorization policy, profile status/role checks, invitation expiry decisions, chat generation, embedding, feedback reranking, and cross-provider ingestion/retrieval/deletion orchestration are not Supabase adapter responsibilities.

## Registration

Add the infrastructure project reference to the host and configure it once in the composition root:

```csharp
using CFC.ChatAI.Supabase;

services.AddSupabaseInfrastructure(options =>
{
    options.Url = configuration["Supabase:Url"]!;
    options.AnonKey = configuration["Supabase:AnonKey"]!;
    options.ServiceRoleKey = configuration["Supabase:ServiceRoleKey"]!;
    options.DocumentsBucket = configuration["Supabase:DocumentsBucket"] ?? "cfc-docs";
    options.VideosBucket = configuration["Supabase:VideosBucket"] ?? "cfc-videos";
});
```

Configuration requires the project URL, anon key, and service-role key. Supply secrets from environment/secret storage; do not commit them. The integration registers stateless singleton adapters over `IHttpClientFactory`; it does not mutate a shared Supabase session. Consumers inject only the relevant abstraction, e.g. `IChunkRepository` rather than a provider-wide facade.

## Security boundaries

- `IIdentityGateway.GetUserAsync` sends the supplied user token with the anon API key. It returns `null` for an invalid/expired token; transport/server failures remain failures, not invalid-credential results.
- Password recovery also uses the anon key.
- `IAdminIdentityGateway`, `IAdminProfileRepository`, `IInvitationRepository`, and the chunk, feedback, and object-storage adapters use the service-role key. Treat these as trusted backend capabilities and inject them only into server-side workflows. They bypass ordinary RLS enforcement; an interface is not an authorization check. The feedback current-user read explicitly filters by user ID but is still service-role scoped.
- `IUserProfileRepository` uses the anon key plus an explicit caller token per request and filters by the supplied user ID. `IConversationRepository` similarly uses the anon key + caller token for every operation; session-table operations carry an explicit owner filter. Message rows have no owner column, so their isolation depends on the session ownership check and deployed message/session RLS policies. The adapter's profile field allow-list does not replace database-side grants/column restrictions; the supplied profile privilege export shows a separate blocker that must be fixed and integration-tested before production self-service writes.
- Never pass service-role credentials, access tokens, or signed URLs to frontend configuration or logs. Validate caller permissions and ownership above these adapters.
- Profile status/role validation is not folded into token validation. Existing account policy must remain in ASP.NET authentication/authorization and profile application services when migrated.

User-scoped profile calls attach the supplied token to that request only; no shared client session is changed. Invitation lookups and writes are privileged backend calls, matching the existing Python use of the service-role Supabase client. Never use these capabilities without application authorization.

## Persistence behavior and guarantees

### Chunks

`SupabaseChunkRepository` maps the public `Chunk` fields to the current `document_chunks` columns (`chunk_id`, `doc_id`, `content`, section/image/source fields, and video/transcript fields). It hydrates full chunks separately from Pinecone and has no reference to Pinecone. Upserts are sent in batches of 500; ID filters/deletes are batched in groups of 100. Multi-batch work is not a transaction: an exception can follow earlier successful batches and callers must reconcile/retry using stable IDs.

A missing requested ID is omitted from `GetByIdsAsync`; this is distinct from a failed request. `ListIdsForDocumentAsync` uses offset/limit pagination and sets `HasMore` when a full page is returned. Callers must continue until `HasMore` is false.

### Conversations

`IConversationRepository` exposes bounded, user-token session listing (including optional count/preview detail), session create/read/title update/delete, ordered history, last-N chat context, message insertion with JSON metadata, and assistant-message/preceding-user context for feedback processing. Lists are stably ordered and detailed lists cap a page at 100 sessions. Context reads return chronological order. Session table queries include `user_id`; message queries first verify the owning session and use the caller token, so correct database RLS remains essential.

Session deletion intentionally sends message deletion followed by session deletion as two requests, matching observed Python behavior. It is not transactional; a failure in the second request raises `SessionDeletionPartialFailureException` to signal that messages may already have been removed. Chat generation, query embeddings, feedback event generation, and endpoint authorization remain application workflow concerns.

### Feedback

The vote operation calls `submit_message_feedback` once with the current argument names (`p_message_id`, `p_user_id`, `p_new_rating`), preserving the atomic database operation used by Python. `GetUserScoresForMessagesAsync` returns only non-null scores for a caller-supplied user ID and bounded message IDs; it does not establish message/session ownership. Global and query-aware scores map to `chunk_feedback_scores` and `get_query_aware_chunk_scores`; query events map to `chunk_feedback_events`.

Query-event delete and insert are deliberately separate methods, not a claimed atomic replace. A failed event sync can be partial; it remains a best-effort workflow concern. Embeddings must be prepared above the repository. The deployed RPC SQL, argument types, RLS/grants, and vector dimension must be verified against the target Supabase project before rollout.

### Profiles and invitations

Profile self-service operations use the caller token and only serialize `full_name`/`avatar_url`; privileged profile operations and all invitation operations use service-role credentials. The adapter does not make account or invitation policy decisions.

`IInvitationRepository` provides latest-by-email and latest-unregistered-by-email reads, insertion returning the persisted row (including database-generated expiry), deletion by ID, and deletion by email. The application compares UTC expiry timestamps and decides `active`, `expired`, `registered`, or `none`. Replacing an expired invitation is a separate delete then insert, not a transaction; concurrent inserts are arbitrated only if the target database has the expected unique index. A recognized SQLSTATE `23505` for `idx_one_active_invite_per_email` becomes `InvitationConflictException`; unrelated failures remain sanitized dependency failures.

The supplied schema excerpt shows a seven-day `expires_at` default and invitation fields, but does not confirm the active-invitation partial unique index, its exact name/predicate, source project identity, or deployed grants. Verify these against the target project before rollout. Profile email lookup is exposed through `IAdminProfileRepository.GetByEmailAsync` for invitation workflows.

### Object storage

`StorageArea` is a logical documents/videos choice; bucket names stay in configuration. A listing returns direct children, including folder entries, with pagination. Recursive cleanup must traverse folders and all pages or use a durable object manifest. `DeleteAsync` deletes only storage keys; it does not delete database chunks or vectors. Deleting several objects is not an atomic cross-bucket operation.

Upload leaves the input stream owned by the caller. Download returns a stream owned by the caller; dispose `DownloadedObject` (or its stream) to release the HTTP response. Signed URLs are time-limited. `GetPublicUrl` only constructs a URL—it does not prove that the object exists or that its bucket is public.

## Failure and retry policy

The adapters surface sanitized HTTP/network failures as `DependencyFailureException`; response bodies and credentials are not included. Only identity 401/403 becomes an invalid identity result; admin 404 becomes absent user. Cancellation propagates. Adapters batch some bulk operations, so completed earlier batches are not rolled back. Workflows own cross-provider reconciliation and business-level fail-open decisions (for example, optional feedback enrichment in retrieval).

Retries must be operation-specific. Stable chunk IDs make upsert retry-friendly, but do not make password recovery, vote changes, or object operations universally idempotent. Do not treat a Pinecone + Supabase workflow as a transaction.

## Operational readiness and configuration

`AddSupabaseInfrastructure` validates its options at registration time: the URL must be an absolute HTTP(S) URL with no embedded user information, query, or fragment; both API keys and bucket names must be non-empty; and the request timeout must be greater than zero and no more than five minutes. HTTP is allowed for local development. Use HTTPS for every non-local deployment. Validation checks shape/presence only; it does not verify that keys, buckets, policies, or the project URL belong together.

- **Secrets:** inject the anon and service-role keys from a deployment secret manager/environment configuration. Never commit them, put them in public configuration, include them in logs/telemetry, or return them in API responses. The service-role key bypasses ordinary RLS and must remain server-side; rotate it through the deployment's secret-management process if exposed. The anon key is not a substitute for database authorization.
- **Buckets:** explicitly verify `DocumentsBucket` and `VideosBucket` against the target project. Defaults (`cfc-docs`, `cfc-videos`) are convenience defaults, not proof those buckets exist or have suitable privacy/policies. Confirm expected key layouts and object size/overwrite policy, and test recursive traversal at the application layer; adapter listing returns direct children only. Keep signed URL lifetimes short and do not log or persist signed URLs as durable links.
- **RLS and grants:** test with the actual caller JWT and anon key, not only the service role. RLS row policies do not restrict columns. The supplied profile privilege export shows all-column authenticated INSERT/UPDATE privileges with self-row policies, so self-service profile writes remain a production blocker until the database ACL/policy design is corrected and direct negative tests prove privileged fields cannot be changed or inserted. Conversation policies/grants, feedback and RPC privileges, invitation rules, and service-role access must likewise be checked against the deployment target.
- **Rate limits and retries:** respect Supabase/Auth/Storage rate-limit responses and `Retry-After` where available at the application boundary. This package does not automatically retry. Apply bounded, operation-specific retry/backoff only where the operation is safe to repeat; do not blindly retry password recovery, vote changes, deletes, or multi-step workflows. A timeout can occur after the server committed a write, so reconcile state before retrying non-idempotent requests. Keep concurrency bounded and surface sanitized dependency failures.
- **Timeouts and monitoring:** the default request timeout is 30 seconds (maximum five minutes) and covers the response-header wait; streaming body reads remain caller-cancellation-aware. Monitor sanitized operation outcomes, latency, throttling, and timeout counts without recording credentials, user tokens, request bodies, PII, or signed URLs.

Configuration cannot establish deployment readiness. Use a non-production project for integration checks and do not describe unit-test success as proof of RLS, schema, RPC, or Storage correctness.

## Validation status and migration prerequisites

The HTTP-handler unit tests (`dotnet/tests/CFC.ChatAI.Supabase.Tests`) run without a live Supabase project; the suite passes (25 tests as of the Phase 6 documentation run). No target-project integration test was run. Before production migration, verify deployed table columns, the invitation partial unique index and expiry default, chat-session/message RLS and insert/delete grants, the chat-message foreign-key delete behavior, RPC definitions/grants, Storage bucket policies/path layouts, and each endpoint's user-vs-service-role behavior. Add live-environment integration tests for profile ACLs, invitation races, cross-user session/message access, session deletion, admin account lifecycle, expired signed URLs, object pagination, partial failures, and deployed RPC transaction semantics.

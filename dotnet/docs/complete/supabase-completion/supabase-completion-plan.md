# Supabase infrastructure completion plan

## Purpose

Build `dotnet/src/CFC.ChatAI.Supabase/` into a safe, cohesive persistence and provider-integration base for the .NET API endpoints that use Supabase. This is a plan, not an implementation or schema migration.

The module may call **only Supabase**. In particular, it must not reference, construct, or call Pinecone, embedding/model services, email services, application services, or API endpoints. Supabase adapters perform Supabase Auth, PostgREST/database, and Storage operations only. Endpoint/application workflows own authorization policy, business rules, embeddings, and coordination with other services.

## Current baseline

The module already provides:

- `IIdentityGateway`: access-token validation and password recovery.
- `IAdminIdentityGateway`: admin user lookup, ban/unban, session revocation, and deletion.
- `IChunkRepository`: chunk upsert/hydration, paged chunk-ID lookup by document, and delete by chunk IDs.
- `IFeedbackRepository`: atomic vote submission, global/query-aware scores, and query-event insert/delete operations.
- `IObjectStorage`: upload, download, paged listing, delete, signed URL, and public URL operations.
- A shared HTTP transport, configuration, and `AddSupabaseInfrastructure` registration.

The current adapters are stateless and do not use Pinecone. Preserve those boundaries. The main missing persistence capabilities are profile access, invitation queries/writes, conversation/session/message operations, a feedback read, and document-level chunk queries.

## Non-negotiable design rules

1. **Supabase-only dependency direction.** `CFC.ChatAI.Supabase` must not reference any other provider or application project. Keep Pinecone out of its project references, namespaces, DTOs, tests, DI registration, and transport.
2. **No cross-provider workflows in adapters.** Do not add methods such as `DeleteDocumentEverywhere`, `SearchAndHydrate`, or `IngestAndStore`. Those workflows belong above the Supabase and Pinecone adapters.
3. **No provider-wide service locator.** Add focused contracts and inject the required one(s); do not expose a broad `ISupabaseService` root.
4. **No shared mutable user session.** User access tokens must be supplied/scoped per request. Never change authorization state on a singleton HTTP client or adapter.
5. **Least privilege.** User-scoped operations use the caller's access token and anon key so RLS remains effective. RLS filters rows, not columns: review SQL grants as well as policy expressions, and do not assume an adapter field allow-list protects direct PostgREST callers. Service-role operations are distinct privileged capabilities and must only be used by trusted application workflows after authorization.
6. **Do not move business policy into repositories.** Repositories do not decide if an invitation is expired, whether a user may change a role, or whether an account is eligible. They expose focused persistence operations; application services enforce policy.
7. **Preserve deployed behavior.** Verify schemas, RLS policies, grants, RPC signatures, foreign keys, and Storage paths before relying on them. Do not infer atomicity or cascade behavior from endpoint comments. Supplied profile exports currently show a security blocker: `authenticated` has effective INSERT/UPDATE privileges on every `profiles` column, while self-row policies only constrain `id`. Do not enable production self-service profile writes until database-side column restrictions are reviewed and tested.
8. **Typed, bounded, async operations.** Use application-owned DTOs, pagination, cancellation, explicit not-found semantics, and sanitized provider failures. Do not expose Supabase HTTP/wire response types.

## Planned capabilities

### 1. Explicit user-scoped access support

Add a safe way to perform PostgREST requests using an individual user's token without mutable shared state. Prefer an immutable per-request scope/client created from the token, or pass an explicit user context to a narrowly scoped operation. The exact API should be chosen after checking how the API host obtains the bearer token and how current RLS policies are intended to work.

Requirements:

- The access token is attached only to that request; the anon key remains the API key.
- The service-role key is never substituted for a user's bearer token in user-scoped calls.
- The user token is not retained in singleton state, logged, serialized in errors, or exposed to frontend code.
- Token validation (`IIdentityGateway`) remains distinct from profile status/role lookup.
- Preserve transport timeouts, cancellation, sanitized failures, and testability through `HttpMessageHandler` injection.

If the application intentionally uses service-role access for a particular workflow (as some existing chat code does), make that an explicit privileged path with explicit user/session filters and authorization above the repository. Do not silently change the data-access model during migration.

### 2. Profile persistence

Add focused contracts, likely separating user self-service from privileged administration to make the trust boundary visible:

- **User profile operations:** get the current user's profile; update only allowed self-service fields (`full_name`, `avatar_url`). Scope these operations to the authenticated user/RLS.
- **Admin profile operations:** list profiles; fetch a profile's role/status and required account metadata; update role; set/clear status, `deleted_at`, and `deleted_by` fields.

DTOs should model the fields used by the API (including `id`, email, name/avatar, role, status, created/deleted metadata). Do not let a self-service update DTO accept role/status/deletion fields. Keep admin policy and safeguards (self-action/superuser checks, allowed roles) in the application layer.

### 3. Invitation persistence

Add an `IInvitationRepository` (or equivalent focused contract) for the actual database operations used by the endpoints:

- Find a profile by email (or add that operation to the appropriate profile repository).
- Find the latest invitation for an email.
- Find an unregistered invitation for an email.
- Insert an invitation and return database-generated fields, including expiry.
- Delete an expired/replaced invitation by ID and delete invitation records by email when required for account deletion.

The repository should expose unique-constraint conflicts as a typed, sanitized persistence result/exception so the application can map the active-invite race to HTTP 409. Expiry comparison and statuses (`active`, `expired`, `registered`, `none`) belong in application logic using UTC. Confirm the partial unique index and database default for `expires_at` exist in the target project; do not add schema changes implicitly.

### 4. Conversation/session/message persistence

Add an `IConversationRepository` or separate session/message contracts if implementation size or security scopes justify it. Cover endpoint operations, not generic table CRUD:

- List a user's sessions in descending creation order.
- Create a session and return the persisted row.
- Get a session by ID with ownership enforced by RLS or an explicit user filter.
- Update a session title.
- Delete a session and its messages, preserving the deployed FK/cascade semantics. If deletion requires two operations, document partial-failure behavior; do not claim transactionality.
- Read ordered messages for history.
- Read the last N role/content messages for chat context.
- Insert user and assistant messages, including JSON metadata (citations and relevant images).
- Read a message/session metadata needed for feedback event generation: assistant message metadata/timestamp and the preceding user message.
- Support detail-list data (message count and last user-message preview) for the session history UI.

Keep ownership and access scopes explicit. Do not rely on a prior ownership read as the sole protection if an operation can include an owner filter or RLS. Confirm that the deployed policies permit the intended user-scoped inserts and deletes before migrating endpoints.

### 5. Feedback read capability

Extend `IFeedbackRepository` with the read used by `GET /api/chat/feedback`: retrieve the current user's non-null ratings for a supplied bounded set of message IDs and return a `message_id -> score` mapping. Message/session ownership checks remain in the conversation/application workflow.

Retain existing behavior:

- `SubmitMessageFeedbackAsync` is one RPC call preserving the transactional vote/aggregate operation.
- Query-aware scoring and query-event persistence remain database-only operations.
- Embeddings are created outside this project; this module only serializes a supplied embedding for the deployed RPC.
- Event deletion plus insertion is not atomic unless a verified transactional database operation is added. Do not relabel it as atomic.

Verify RPC names, parameter names/types, vector dimensions, grants, and deployed transaction semantics before production use.

### 6. Document-level chunk queries

Extend `IChunkRepository` only as needed for the current document endpoints:

- Fetch a document's source and source type (one-row lookup).
- List document summaries with `doc_id`, source, source type, and chunk count.

The current API computes summaries by paging through `document_chunks` and aggregating rows. An initial implementation can preserve this behavior with bounded pagination. Consider a database view/RPC only if volume makes client-side aggregation inappropriate and the schema change is separately reviewed. Keep chunk deletion keyed to Supabase chunk/document data; never delete vectors from this project.

### 7. Storage parity and verification

The current `IObjectStorage` contract appears to cover active Supabase Storage needs. Before declaring parity, verify that existing operations support:

- Correct configured document/video bucket mapping and the deployed object path layouts.
- Replace/overwrite semantics where required by upload flows.
- Downloading image/transcript/file objects by validated object key.
- Pagination and traversal of nested folders for cleanup/listing workflows.
- Short-lived signed URLs and clear stream ownership/disposal.

Keep recursive traversal and document lifecycle coordination in an application workflow; `IObjectStorage` must not delete database rows or vectors. Add a capability only if endpoint parity tests demonstrate a real gap.

## Endpoint coverage map

| Endpoint group | Supabase support required | Status after planned work |
|---|---|---|
| Health and public auth configuration | Connectivity/configuration only; no new repository needed | Existing health/config endpoints remain host concerns |
| Forgot password / token validation | `IIdentityGateway` | Already covered; verify response/rate-limit behavior at API boundary |
| Email eligibility and admin invitations | Invitation reads/inserts/deletes and profile email lookup | Add invitation/profile persistence |
| Profile self-service and current-user authorization | User-scoped profile operations, role/status lookup | Add profile persistence and user-scoped transport |
| Admin user listing/role/deactivate/reactivate/delete | Admin profile queries/updates plus existing admin Auth operations | Add admin profile persistence; existing Auth operations cover most identity actions |
| Sessions and chat messages | Session/message list/create/read/update/delete/insert/history operations | Add conversation persistence |
| Feedback submit/read | Existing atomic submit and event/score methods plus user feedback read | Extend feedback persistence |
| Ingestion/search/document hydration | Existing chunk operations, with document metadata/list additions | Extend chunk persistence for admin document listing/lookups |
| Upload/download/image/video Storage | Existing object operations and configured buckets | Verify parity; add no cross-provider behavior |
| Pinecone/embedding/LLM/local settings | Not Supabase responsibilities | Must remain outside this project |

## Implementation phases

### Phase 0 — Contract and database reconnaissance

1. Inventory the active routes in `main.py` and cross-check `local/csharp-endpoint-migration-chunks.md`.
2. Capture actual table columns, constraints, indexes, RLS policies, grants, foreign keys/cascades, RPC definitions, and Storage policies/path examples from the target Supabase project. Sanitized profile exports have been supplied; confirm their project identity and inspect effective/table/column ACLs and any profile-creation trigger.
3. Record for every operation whether Python currently uses a user token, anon key, or service-role key. Mark intentional deviations and decide whether .NET preserves them or deliberately changes them.
4. Write contract DTOs and expected absence/conflict/failure semantics before implementing HTTP calls.
5. Resolve the known profile write escalation: restrict user writes to intended fields at the database permission/trigger boundary, preserve service-role admin operations, and verify with direct authenticated PostgREST negative tests.

**Exit criteria:** reviewed endpoint-to-operation matrix; no undocumented schema assumptions; user-vs-service-role behavior is decided; profile self-service cannot write privileged columns through the database API.

### Phase 1 — Transport and security foundation

1. Implement immutable request-level bearer authorization support in the internal transport.
2. Keep separate helpers for anon + user token and service-role requests; never infer privilege from the endpoint URL.
3. Ensure safe URI/query construction, cancellation, bounded timeouts, sanitized errors, and no credential/body leakage.
4. Add tests proving concurrent requests with distinct user tokens cannot leak authorization headers.

**Exit criteria:** transport tests cover both authorization modes, cancellation, failures, and concurrency; project remains independent of Pinecone and other providers.

### Phase 2 — Profiles and account reads

1. Add user-profile and admin-profile contracts/DTOs and internal adapters.
2. Implement self-read/self-update with only allowed columns and user-scoped credentials. The adapter allow-list is defense-in-depth; database ACLs/policies must also prevent direct clients from setting privileged columns.
3. Implement admin list/read/update operations with service-role credentials and bounded pagination.
4. Add tests for missing profiles, empty updates, forbidden self-service fields, paging, and sanitized database errors. Integration tests must also prove direct user-token requests cannot change `role`, `status`, `deleted_at`, or `deleted_by`, or insert a profile with privileged values.

**Exit criteria:** profile endpoints and authentication/authorization profile lookups have persistence coverage without merging token validation and profile policy; database-level self-service write restrictions are verified while service-role admin operations continue to work.

### Phase 3 — Invitations

1. Add invitation DTOs/contracts and queries/inserts/deletes.
2. Return the inserted row's DB-generated expiry and map known unique violations to a typed conflict.
3. Test active, expired, registered, absent, concurrent insert, and delete-by-email cases using deterministic clocks in the application layer.

**Exit criteria:** check-email and admin invitation endpoints can be implemented without direct Supabase calls outside the module; invitation business decisions remain in application services.

### Phase 4 — Conversations and feedback read

1. Add session/message persistence contracts and user-scoped implementation.
2. Implement ordered history, last-N context retrieval, session detail projections, metadata JSON handling, and ownership-filtered operations.
3. Add the current-user feedback lookup to `IFeedbackRepository`.
4. Verify insert/delete/RLS/FK behavior in a live or controlled Supabase integration environment; test partial failure where workflows use multiple requests.

**Exit criteria:** session/chat/feedback endpoints can use injected contracts; no repository method performs chat generation, embedding, or cross-provider retrieval.

### Phase 5 — Document queries and parity audit

1. Add document summaries and document metadata lookup to chunk persistence.
2. Audit storage path/bucket behavior against real document, video, and image objects; verify recursive listing pagination and signed URL expiry.
3. Run the endpoint coverage matrix and mark each active endpoint as covered, intentionally non-Supabase, or blocked by a verified schema issue.

**Exit criteria:** no active endpoint needs direct PostgREST/Auth/Storage calls outside `CFC.ChatAI.Supabase` for the capabilities in scope; cross-provider coordination remains above the package.

### Phase 6 — Documentation and operational readiness

1. Update `dotnet/docs/supabase/README.md` with the new contracts, authorization modes, DI registrations, persistence guarantees, and remaining schema prerequisites.
2. Add safe configuration validation and operational guidance for secrets, buckets, RLS, rate limits, and retries.
3. Keep real credentials and environment-specific policy details out of source control.

**Exit criteria:** the README matches implemented behavior and a new endpoint can identify its persistence contract without reading adapter internals.

## Testing strategy

### Unit/adapter tests

Use the existing recording `HttpMessageHandler` pattern in `dotnet/tests/CFC.ChatAI.Supabase.Tests` to verify:

- HTTP method, path, query encoding, headers, JSON field mapping, and response parsing.
- User-token versus service-role authorization, including concurrent distinct user scopes.
- Empty results versus not-found versus dependency failure.
- Pagination boundaries and stable ordering.
- PostgREST filters for owner/user IDs and field allow-lists for self-service updates.
- Unique invitation conflict translation and RPC argument compatibility.
- JSON/metadata round trips, nullable fields, and timestamp handling.
- Stream ownership, storage paths, signed URL response parsing, and paginated listing.
- Cancellation and sanitized exceptions.

### Application/contract tests

The API/application projects should test authorization and behavior using fakes: inactive-user handling, admin/superuser/self-action rules, invitation expiry and races, session ownership, feedback rating validation, and endpoint response contracts. These are not adapter policy tests.

### Integration tests

Before rollout, run tests against a non-production Supabase project with representative RLS, grants, constraints, RPCs, buckets, and foreign keys. Include concurrent invitation writes, cross-user access attempts, session deletion, admin account lifecycle, expired signed URLs, object pagination, and partial failures. Never make unit tests depend on a live project or committed credentials.

### Dependency boundary checks

- Verify the `.Supabase` project file contains no Pinecone, API, or application project reference.
- Keep provider-agnostic HTTP/wire mapping inside this project; do not import Pinecone SDK types.
- Add a lightweight CI check or architecture test to reject project references/namespaces for Pinecone and other provider packages in `CFC.ChatAI.Supabase`.
- Test the module independently of the API, Pinecone, vector indexes, and model assets.

## Completion criteria

The Supabase module is a solid base when:

- All planned operations are exposed through small typed contracts and internal Supabase-only implementations.
- All routes that require Supabase persistence can be implemented without direct Supabase SDK/REST calls in endpoints or application services.
- User-scoped and privileged calls are separated and tested; no token/session state is shared across requests.
- Database grants and RLS jointly enforce profile least privilege; user-token integration tests reject privileged profile writes and service-role admin integration tests pass.
- Existing chunk, feedback, identity, and Storage behavior remains covered and documented.
- Database/RLS/RPC/Storage assumptions are verified against the deployment target.
- The project and its tests have no dependency or runtime call path to Pinecone, embedding/model services, or any other external provider.
- Cross-provider/document/chat workflows remain outside `.Supabase/` and explicitly handle partial failure rather than claiming distributed transactions.

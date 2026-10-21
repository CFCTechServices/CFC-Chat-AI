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
| `IChunkRepository` | Upsert chunks, hydrate rows by IDs, list document chunk IDs by page, and delete by IDs |
| `IFeedbackRepository` | Atomic feedback submission RPC, global/query-aware scores, and query-event persistence |
| `IObjectStorage` | Upload/download/list/delete objects and create signed/public URLs |

Supabase-facing models and contracts are organized by concern under `Identity/`, `Chunks/`, `Feedback/`, `Storage/`, and `Common/` in `dotnet/src/CFC.ChatAI.Supabase`. Database operations are grouped by behavior rather than mechanically exposing one repository per table.

There are intentionally no interfaces yet for profiles, invitations, or conversations: add those when the associated endpoints are migrated. Authorization policy, profile status/role checks, embedding, feedback reranking, and cross-provider ingestion/retrieval/deletion orchestration are not Supabase adapter responsibilities.

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
- `IAdminIdentityGateway` and the chunk, feedback, and object-storage adapters use the service-role key. Treat these as trusted backend capabilities and inject them only into server-side workflows. They bypass ordinary RLS enforcement; an interface is not an authorization check.
- Never pass service-role credentials, access tokens, or signed URLs to frontend configuration or logs. Validate caller permissions and ownership above these adapters.
- Profile status/role validation is not folded into token validation. Existing account policy must remain in ASP.NET authentication/authorization and profile application services when migrated.

This initial implementation covers backend ingestion/retrieval and privileged Auth operations. It does **not** replace user-scoped profile/session access. Add separate request-scoped user-token adapters before migrating an endpoint whose behavior depends on RLS; never change a shared client's session per request.

## Persistence behavior and guarantees

### Chunks

`SupabaseChunkRepository` maps the public `Chunk` fields to the current `document_chunks` columns (`chunk_id`, `doc_id`, `content`, section/image/source fields, and video/transcript fields). It hydrates full chunks separately from Pinecone and has no reference to Pinecone. Upserts are sent in batches of 500; ID filters/deletes are batched in groups of 100. Multi-batch work is not a transaction: an exception can follow earlier successful batches and callers must reconcile/retry using stable IDs.

A missing requested ID is omitted from `GetByIdsAsync`; this is distinct from a failed request. `ListIdsForDocumentAsync` uses offset/limit pagination and sets `HasMore` when a full page is returned. Callers must continue until `HasMore` is false.

### Feedback

The vote operation calls `submit_message_feedback` once with the current argument names (`p_message_id`, `p_user_id`, `p_new_rating`), preserving the atomic database operation used by Python. Global and query-aware scores map to `chunk_feedback_scores` and `get_query_aware_chunk_scores`; query events map to `chunk_feedback_events`.

Query-event delete and insert are deliberately separate methods, not a claimed atomic replace. A failed event sync can be partial; it remains a best-effort workflow concern. Embeddings must be prepared above the repository. The deployed RPC SQL, argument types, RLS/grants, and vector dimension must be verified against the target Supabase project before rollout.

### Object storage

`StorageArea` is a logical documents/videos choice; bucket names stay in configuration. A listing returns direct children, including folder entries, with pagination. Recursive cleanup must traverse folders and all pages or use a durable object manifest. `DeleteAsync` deletes only storage keys; it does not delete database chunks or vectors. Deleting several objects is not an atomic cross-bucket operation.

Upload leaves the input stream owned by the caller. Download returns a stream owned by the caller; dispose `DownloadedObject` (or its stream) to release the HTTP response. Signed URLs are time-limited. `GetPublicUrl` only constructs a URL—it does not prove that the object exists or that its bucket is public.

## Failure and retry policy

The adapters surface sanitized HTTP/network failures as `DependencyFailureException`; response bodies and credentials are not included. Only identity 401/403 becomes an invalid identity result; admin 404 becomes absent user. Cancellation propagates. Adapters batch some bulk operations, so completed earlier batches are not rolled back. Workflows own cross-provider reconciliation and business-level fail-open decisions (for example, optional feedback enrichment in retrieval).

Retries must be operation-specific. Stable chunk IDs make upsert retry-friendly, but do not make password recovery, vote changes, or object operations universally idempotent. Do not treat a Pinecone + Supabase workflow as a transaction.

## Validation status and migration prerequisites

The adapters and HTTP-handler unit tests (`dotnet/tests/CFC.ChatAI.Supabase.Tests`) are wired into the solution, but this environment did not provide the `dotnet` executable, so build/test execution could not be performed here. Before production migration, verify the deployed table columns, RPC definitions and grants, Storage bucket policies/path layouts, and the selected endpoint's user-vs-service-role behavior. Add live-environment integration tests for RLS/authorization, concurrent requests, pagination, partial failures, and the deployed RPC transaction semantics.

# Plan: .NET document deletion endpoints

**Status: implemented with revised development-only scope; original plan below is historical.** The user explicitly waived durable recovery and cross-instance coordination. The current routes therefore do **not** satisfy the original recovery, concurrency, missing-metadata, or live acceptance criteria below. See [changes, limitations and verification](document-deletion-changes.md). Browser and live-provider acceptance remain outstanding. This is not approval for production cutover. See the [remaining API plan](../../plan/internal-api/internal-api-remaining-endpoints-plan.md) and [document upload plan](../document-upload/document-upload-endpoints-plan.md).

## Current state and scope

- `IDocumentHandler.DeleteAsync` already lists chunk IDs, deletes their Pinecone vectors, deletes the Supabase chunk rows, then recursively deletes the document's Storage tree. `DocumentDeletionResult` reports `Found`, chunk count and Storage object count. The providers do not share a transaction. There is an ingest/delete happy-path fake-provider test, but this is not proof of safe recovery or live-provider behavior.
- The .NET API currently has admin document list and download routes, but no delete route. The existing `web/components/admin/content.jsx` delete action already calls the Python-compatible admin URL with a bearer token and a confirmation modal; verify its actual hosting and response handling rather than assuming browser routing is configured.
- The Python delete route catches provider errors and can report success despite incomplete deletion. Do not reuse that behavior or claim deletion has succeeded merely because a request was sent.
- The handler also supports video metadata and Storage trees. This plan must not inadvertently expose unsafe video deletion: either verify video/artifact behavior with equivalent tests and include it explicitly, or reject known video documents before destructive work and document the limitation. Handle missing metadata and partial recovery without silently bypassing that decision.

## Route contracts

| Route | Purpose | Access |
|---|---|---|
| `DELETE /api/admin/documents/{docId}` | Frontend Admin → Content deletion | Existing `admin` policy: authenticated, active admin or superuser |
| `DELETE /api/ingest/dev/{docId}` | Local operator development deletion | Unauthenticated; separate explicit opt-in, Development environment, direct loopback only |

- Both routes call the same deletion workflow, accept only a document ID (no arbitrary Storage path, filename, filter, wildcard, or bulk delete), and use the same validation and recovery semantics. The dev route is not a shortcut around deletion safety.
- Validate IDs consistently with admin download and the handler: 1–128 ASCII letters/digits, `-`, `_`, or `.`, excluding `.` and `..`, path separators, control characters and encoded path tricks. Share the validation rather than duplicating subtly different rules. Reject invalid input before touching providers.
- Map routes only when required Supabase and Pinecone dependencies are configured, consistent with existing provider-backed routes. Nonempty configuration does not establish connectivity. Return sanitized `{ "detail": "..." }` errors; never expose provider exception text, credentials, document contents, or sensitive paths.
- Return HTTP 200 with Python-compatible `{ "success": true, "message": "Document deleted." }` only after all required cleanup completes. An absent ID with no pending cleanup returns 404, including a repeat after confirmed complete deletion. A pending or uncertain operation is **not** an ordinary 404: resume it safely or report a non-success requiring reconciliation. Reject unauthorized callers with 401/403. For a conflicting in-progress operation return 409; for known unavailable dependencies use 503, and for partial/uncertain failures use a sanitized non-2xx response with a correlation/operation ID. Do not promise atomicity.
- Pass cancellation through; a cancellation or lost HTTP response may occur after side effects. Preserve recoverable state and make retries safe rather than assuming cancellation means nothing happened.

## Recovery and concurrency prerequisites

1. Keep orchestration in `DocumentHandler`, not duplicated in the HTTP routes. Before destructive work, durably record the operation ID, target, provider/namespace context, source type, complete chunk/vector IDs and stage. Ensure the record is safely persisted before any provider deletion, and retain it until cleanup is confirmed. Do not store credentials or document contents. Define access controls and retention for operation records.
2. Make each stage safely retryable across process crashes and uncertain provider responses. Resume using the recorded IDs even if chunk rows are already gone; otherwise a retry can no longer discover orphan vectors. Treat failed or unverifiable provider operations as incomplete, not success. Search both Storage areas as appropriate when metadata is missing, without using missing metadata as proof that vectors are absent. For older orphaned data with no operation record or discoverable IDs, require explicit operator reconciliation rather than false success or 404.
3. Coordinate deletion with ingest, replacement and other deletes for the same ID, including across instances: use a durable per-document reservation/lock or equivalent fencing and define recovery after expiry/crash. Both the authenticated and dev upload paths must participate; a process-local lock alone is insufficient. Prevent a retry of an old deletion from deleting a newly ingested document with the same ID. Return a conflict when safe serialization cannot be obtained.
4. Verify Storage pagination, recursive artifact deletion and exact prefix boundaries, including neighboring document IDs; test partial failures after every stage. Preserve enough progress for repair, including when Storage metadata or a prior network response is missing. Record safe diagnostics and operation/stage identifiers server-side. No public endpoint may claim all stores are clean solely because the original metadata row is absent.

## Development guard

- Add an independent flag, e.g. `Api:EnableDevDelete`, default `false` (`Api__EnableDevDelete=true` in environment variables). Register the route only in Development, with this flag enabled and providers configured; `Api:EnableDevIngest` alone must not enable deletion.
- Require the actual connection's IPv4 or IPv6 remote address to be loopback; do not trust caller-supplied forwarded headers. A local proxy can appear as loopback, so never expose or proxy the dev route. Bind the host to loopback; leave the flag disabled on shared, remote, containerized and production deployments. A development flag does not guarantee that configured providers are development providers.
- After implementation, an operator on the host can use `curl -X DELETE http://127.0.0.1:5000/api/ingest/dev/<document-id>`; use `curl.exe` in PowerShell. Check the status and response. Never issue this command against an unverified provider target.

## Frontend integration

- Keep the admin confirmation and encoded ID. Check for a usable session and handle expired/missing tokens, 401/403, 404, 409 and partial/dependency errors without removing the row or displaying success. Remove/refresh the row only after confirmed HTTP success; account for a lost response and tell the operator when reconciliation may be required. Disable repeated actions while the request is in flight.
- Verify that the active page really loads `web/components/admin/content.jsx`, and that its API routing reaches .NET with appropriate same-origin/CORS configuration. Do not proxy the dev route. Do not imply document replacement is implemented.

## Test and acceptance criteria

- Handler tests with fake providers: full deletion, missing and repeated IDs, paginated chunk IDs and Storage trees, neighboring prefixes, original and supported artifacts, each provider failing before and after side effects, cancellation, process restart, retry after chunk rows are gone, uncertain responses, operation-record failures, concurrency and re-ingest fencing. Verify no false success or 404 for unresolved cleanup. Explicitly test the chosen video policy.
- API tests: admin/superuser success, ordinary/inactive/deleted/unauthenticated denial, invalid and encoded IDs, absent document, conflicts, dependency absence, sanitized failures and cancellation. Verify dev flag defaults off, Development requirement, missing-provider behavior, IPv4/IPv6 loopback enforcement, non-loopback rejection and forwarded-header spoofing. Both routes must invoke the same safe workflow.
- Browser smoke tests against a disposable non-production setup: confirmation/cancel, success and list refresh, expired/non-admin sessions, conflict and partial failure, plus actual frontend routing. Verify no client success on a failed or ambiguous response.
- Live acceptance only after confirming the exact disposable Supabase project and document/video buckets, Pinecone index/namespace, and permissions: delete a uniquely named test document; independently verify its original and artifacts, all Supabase chunk rows, and the corresponding Pinecone vectors are gone. Record failures and reconcile before reporting completion. Run fake-based automated tests without live credentials.
- Update the README, endpoint status and frontend migration checklist with actual contracts, flags, recovery procedure and tested limitations. This plan alone is not approval for production cutover.

## Cleanup target from the current smoke test

**Pending; not deleted.** Verify the provider target and permissions before considering cleanup; the recovery protections described above were waived and are not implemented:

- Filename: `test-ea88dc393cb5.txt`
- Document ID: `test-ea88dc393cb5`
- Expected original: bucket `cfc-docs`, key `docs/test-ea88dc393cb5/original/test-ea88dc393cb5.txt`
- The upload returned HTTP 200 with one chunk and one vector. The smoke-test configuration reported Pinecone index `cfc-rag-chatbot`, namespace `cfc-knowledge-base`. Verify the actual Supabase project and current Pinecone target before any destructive call; do not assume they are disposable merely because the dev endpoint was used.

After deletion, independently check the exact original/other artifacts, chunk row(s), and corresponding vector(s), and record the result or any required reconciliation. Do not delete this document merely by writing this plan.

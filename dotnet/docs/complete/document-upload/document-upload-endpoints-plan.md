# Plan: .NET document upload endpoints

**Status: implemented; external browser/provider acceptance remains outstanding.** See [changes and verification](document-upload-changes.md). This is a development-oriented increment, not approval for production cutover. It adds document ingestion through the existing `IDocumentHandler`, with separate frontend and local operator routes. It does not implement video uploads, document replacement/deletion, or an arbitrary server-side file path endpoint.

## Goals and route contracts

| Route | Purpose | Access | Input |
|---|---|---|---|
| `POST /api/files/upload` | Website's Admin → Upload flows | Existing `admin` policy: authenticated, active admin or superuser | `multipart/form-data`, exactly one `file` |
| `POST /api/ingest/dev` | Local, lower-validation curl workflow | Unauthenticated; explicit opt-in, Development environment, direct loopback only | `multipart/form-data`, exactly one `file` |

Both routes accept one uploaded **document**, synchronously call the same `IDocumentHandler.IngestAsync` workflow and return the same success DTO. The dev route does not bypass the processor or handler: "unvalidated" means it skips the frontend route's extra business checks, not that it accepts arbitrary request shapes, unlimited files, unsupported formats, or inaccessible content. Neither route accepts a filename pointing to a file on the server.

Suggested success response, HTTP 200 only after chunk persistence, vector indexing and original storage all succeed:

```json
{
  "document_id": "sample",
  "source_name": "sample.txt",
  "source_type": "document",
  "chunk_count": 3,
  "vector_count": 3
}
```

Use a dedicated API DTO rather than exposing provider/storage contracts. Return safe, consistent `{ "detail": "..." }` errors: 400 for malformed/missing file or invalid filename, 401/403 for failed frontend authentication/authorization, 409 for frontend duplicate-ID detection, 413 for excess size, 415 for unsupported extensions, 422 for an unreadable document or no extractable text, and 503 for a known unavailable provider. Other failures, including incomplete rollback, return a sanitized 500; log a trace identifier, not credentials or document contents. Do not report success for partial ingestion or expose provider exceptions to clients.

## Shared ingestion and configuration

1. In `dotnet/src/CFC.ChatAI.Api/Program.cs`, register document processing and handling alongside the existing Supabase and Pinecone registrations when their required configuration is present. Configure document limits and optional converter paths; do not require Azure Speech for documents. Map provider-backed upload routes only when their dependencies are configured, consistent with the existing host's absent-route/404 behavior. Nonempty configuration is not a connectivity check.
2. Use `.pdf`, `.docx`, `.doc`, `.txt`, `.md`, and `.eml` as the document extension allowlist. Both endpoints must reject video types because their API contract is document-only; leave video ingestion for separate work. The existing processor defaults to **50 MiB**. Align HTTP multipart/request limits and handler/processor limits, accounting for multipart overhead, and enforce the actual stream limit rather than relying only on `Content-Length`. Avoid unnecessary buffering and dispose upload streams; retain the handler's staging-file cleanup. Reject empty files and malformed multipart on both routes.
3. Invoke the handler with `DocumentHandlerInput` and request cancellation. It stages the file, extracts text/chunks, upserts Supabase chunks and Pinecone vectors, then stores the source in Supabase Storage; it attempts compensating chunk/vector deletion on failure. Those independent writes are **not transactional**. Test and surface failed compensation as failure requiring reconciliation, and don't promise rollback of every Storage-side failure.
4. Verify the selected development Supabase project, buckets, Pinecone index and namespace before live tests. The current host does not pass `PINECONE_NAMESPACE` into its Pinecone options; address or explicitly verify that mismatch before writing to a non-default namespace. The embedding model and vocabulary must match the existing index. Keep all service credentials on the server.
5. For `.doc`, document the LibreOffice dependency and configured path. Follow the processor's actual PDF/OCR behavior and dependencies when documenting scanned PDFs; do not claim support that isn't present.

## Decouple video processing

`DocumentHandler` currently requires `IVideoProcessor` even for text documents, while `AddVideoProcessing` requires Azure Speech endpoint/key. Change composition so `IDocumentHandler` can resolve and ingest documents without a registered video processor. Preserve video behavior when a processor is configured; if a handler receives a video without one, fail explicitly with a safe, recognizable "video processing not configured" error, not a startup failure or success stub. Neither new HTTP route accepts video in this increment. Avoid resolving `IVideoProcessor` eagerly or adding a placeholder Azure credential. Test both configured and unconfigured cases, including DI resolution and existing video-handler behavior.

## Frontend route validation and duplicate handling

- Reuse the existing `admin` policy; authorization must be enforced server-side, not just by the Admin UI. Validate a single safe filename, supported extension, size and non-empty content before ingestion. Reject path components and control characters; the client-supplied MIME type is not proof of format. Let the processor determine whether content can actually be extracted.
- Detect an existing document ID before ingesting and return 409 rather than implicitly replacing it. IDs currently derive from a normalized filename stem in `DocumentProcessor`, whereas each processing run creates new chunk IDs. Share the exact ID derivation with the preflight check instead of copying a divergent slug algorithm; include collisions across extensions and names that normalize alike. Check IDs against the constraints needed by existing admin read/download routes. A preflight lookup alone is not atomic: concurrent uploads can race, so document this dev limitation and do not claim guaranteed uniqueness without a reservation/locking design. Do not delete existing data as part of this endpoint.
- The dev endpoint skips the duplicate-ID preflight. Warn that repeated filenames or normalized-name collisions can add old and new chunks under the same document ID, overwrite the original, and make reconciliation necessary. Use a new filename for each dev test. Never use it for replacement.
- Use ASP.NET multipart handling without introducing an accidental antiforgery requirement for bearer-token/curl API requests; test actual request binding, authorization, size handling and cancellation end to end.

## Local dev route guard

- Add an explicit flag such as `Api:EnableDevIngest`, default `false`; require `IHostEnvironment.IsDevelopment()` and register the route only when both conditions hold. `Api__EnableDevIngest=true` is the environment-variable form. Missing provider configuration also leaves the route unmapped.
- Require the direct connection's remote IP to be IPv4 or IPv6 loopback. Do not trust caller-supplied forwarded headers for this decision. A reverse proxy on the same machine can itself appear as loopback, so **do not expose or proxy this route**: bind the dev host to loopback and keep the flag off in shared, containerized, remotely accessible, and production deployments. Use a separate authenticated operator mechanism if remote access is later needed.
- With the route disabled or outside Development, expect 404. With it enabled, reject non-loopback callers. It remains unauthenticated and intentionally less restrictive, not a general-purpose public endpoint.

Example after implementation, from the host running the API:

```bash
# Set ASPNETCORE_ENVIRONMENT=Development and Api__EnableDevIngest=true first.
curl -F "file=@./new-document.pdf" http://127.0.0.1:5000/api/ingest/dev
```

On Windows PowerShell, use `curl.exe` if `curl` resolves to a PowerShell alias. Use a real file and inspect the response/status; a completed transfer alone does not imply successful ingestion.

## Frontend changes

- Update the active `web/components/admin/upload.jsx` document flows (single, sequential bulk, `.eml`, and PDF) to supply `Authorization: Bearer <session access token>` to the XHR after `open`. Do not set multipart `Content-Type` manually. Handle missing/expired session, 401/403, 409 and other non-2xx results; show success only after the response confirms ingestion. Align the displayed result with the new DTO, not Python's `supabase.warning` response.
- Preserve the existing PDF and EML picker layout unless deliberately changed. Clearly disable or mark video selection/upload as unavailable when targeting the .NET backend; do not present this document route as implementing `/api/videos/upload`. Confirm whether other upload scripts or legacy pages are active before updating them.
- The .NET API does not currently serve the website. Verify the actual dev frontend hosting/proxy arrangement routes `/api/files/upload` to the .NET host, and verify CORS if the browser uses a separate origin. Do not proxy the dev-only route. Python and .NET have different auth and response contracts, so coordinate UI changes with backend routing rather than assuming the updated UI works unchanged against Python.

## Tests and acceptance

- Handler/DI unit tests: document ingestion resolves and succeeds without Azure Speech; configured video processing still delegates; unconfigured video fails clearly; processor/handler size and staging cleanup remain effective.
- HTTP tests with fake handler/repository: admin/superuser success, missing/invalid token and ordinary-user rejection, malformed/missing/multiple file, unsupported type, empty file, size limits, invalid filename and duplicate normalized ID; dev flag/environment/loopback gates and dev duplicate-check bypass. Verify the dev route is absent by default and on non-Development environments. Exercise safe error mappings, cancellation, original-storage failure and failed compensation; no success on partial writes.
- Browser smoke test against .NET routing: single document, bulk, PDF and EML; expired/non-admin sessions, conflict and failed ingestion; no misleading video-upload affordance. Verify successful content appears via authenticated `GET /api/admin/documents` and is retrievable in chat with the same index/namespace.
- Optional live curl smoke test against **development** providers: upload a uniquely named small text file, verify returned ID/counts and corresponding chunks, vectors and original; record any cleanup/reconciliation required. Confirm provider failure doesn't return success. Use mocks for automated tests; don't rely on live credentials in CI.
- Update the API status, README/setup and frontend migration checklist after implementation with actual routes, settings, limitations, and commands. Keep the dev route disabled by default and do not treat these tests as production-readiness proof.

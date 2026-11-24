# Plan: document upload request parity

**Status: completed for the synthetic offline suite.** The real-model case is skipped and C# Pinecone capture remains application-level; see the [report](../../tests/document-upload-parity-report.md) and [PR changes](document-upload-request-parity-changes.md).

## Goal

Run the same document through the unauthenticated Python and C# upload endpoints, intercept what each would send to Supabase and Pinecone, and put **all comparisons in one concise report**. This is an investigation, not an assertion that the implementations should currently match. Do not modify production behavior to make tests pass.

Compare Python `POST /api/files/upload` with C# `POST /api/ingest/dev`. Both accept multipart `file` uploads, but they are not identical contracts: the C# endpoint is Development-only, opt-in, loopback-only and skips the authenticated route's duplicate check. Use in-process test hosts, not exposed dev servers. Python's `/api/ingest/document` takes a filename rather than an upload and is not the primary comparison.

## Deliverable: one report

Generate `docs/tests/document-upload-parity-report.md` with every executed case, in a fixed order. It is the only human-facing output; keep intermediate captures in memory or a cleaned temporary directory. Use synthetic fixtures only, no secrets or real customer documents. Regenerate the entire file on each run; do not append stale results.

Start with a short summary table: case, outcome (`MATCH`, `DIFFERENT`, `ERROR` or `SKIPPED`), and counts of operations/differences. Then give each case:

1. A **one- or two-line description** of what it shows, plus fixture and capture level.
2. A compact comparison of endpoint status and response, ordered provider operations, storage paths/options, chunk rows, vector IDs/metadata/namespace/values, and any errors or missing operations. Show Python and C# values side by side for each difference; state explicitly when both match or when one side did not send anything.
3. Collapsible details containing the captured, sanitized payloads and binary-object size/hash for both sides, so a reader can inspect every operation in the same file. Include complete synthetic JSON/section content and all vector values for the default small fixtures; do not silently truncate. Record binary object hashes, not binary data. Include methods, paths, relevant non-secret headers, query parameters, content types, batching and request order when captured. Identify missing fields separately from `null`.

Use a short per-case difference summary to keep the report scannable even though full details are available. Include a legend for normalization and capture limitations; do not call reconstructed application objects actual HTTP JSON. No separate per-case reports or links to required external artifacts.

## Capture approach and safety

- Submit the same fixture bytes, filename and multipart field through the real endpoint and processor/handler paths in each test host. Do not replace Python `ingest_document` or C# `IDocumentHandler`: existing endpoint tests do so and therefore cannot establish downstream parity.
- Configure dummy provider settings before Python imports its module-level clients/settings and before C# route registration. Use an isolated temporary documents directory and clean it even on failure. Set the same explicit bucket and Pinecone namespace for both where supported. Never load real `.env` credentials into this harness.
- **Preferred evidence: provider transport capture.** Keep the real provider adapters and intercept their outgoing transport after serialization, with canned success responses. Capture Supabase REST and Storage requests and Pinecone data-plane upserts; include any unexpected provider requests in the report. The C# Supabase adapter uses a named `HttpClient`; Pinecone uses an SDK client created inside `PineconeVectorStore`. Investigate suitable SDK transport hooks for both languages and any discovery/control-plane calls before promising wire-level coverage. Verify the captured request bodies are the actual serialized SDK requests, not recreated JSON.
- If a provider's transport cannot be safely intercepted, use a narrowly scoped recording fake at its nearest practical boundary, label its evidence **application-level**, and describe what it cannot prove (SDK serialization, headers, batching or namespace, as applicable). For C# Pinecone, faking `IPineconeService` alone captures text records, not actual vector values; to capture vectors, use a test seam around its internal embedding/vector-store boundary or intercept the SDK. Avoid exposing internal APIs publicly solely for the test. Do not present either fallback as wire-level evidence.
- Use a shared deterministic, small-dimensional test embedding algorithm in both languages for the default suite, recording its inputs and outputs. This makes structural comparisons fast and readable; label vectors as test values. An optional real-model case can compare matching model versions and dimensions with a documented tolerance, but must report `SKIPPED` with the reason if model assets are unavailable. It must not silently substitute fake embeddings.
- Fail closed: block unexpected network traffic, including SDK discovery, and use only test credentials and canned responses. A harness self-test must deliberately attempt an unhandled request and verify it fails rather than reaches the network. Never send requests to real Supabase/Pinecone; redact credentials and authorization headers before capture/reporting.

## Comparison rules

- Compare semantic JSON as structured data (object key order is irrelevant); preserve array order, request order, field presence, types and values. Do not normalize whitespace in document content, hide extra fields, combine batches, or suppress differing paths. Show batching separately from logical row/vector contents.
- IDs can be random. Build per-run aliases for section and chunk IDs in order of first appearance, retaining the original IDs in the captured details. Validate each side's references between section data, chunks and vectors before applying aliases. Compare ordered sections/chunks and their content; if counts or boundaries differ, show unmatched items rather than forcing correspondence. Never use normalization to turn a real relationship or content mismatch into a match.
- Compare binary originals by size, content type, path/options and SHA-256; parse Storage JSON objects as JSON when applicable. Report image artifacts and other one-sided operations. Include the actual namespace and whether it was absent or empty when observable.
- Compare real embeddings numerically only when both sides use the same model/configuration, with a stated absolute/relative tolerance and dimension check. Otherwise mark them not comparable. For deterministic test vectors, compare exactly.
- Distinguish endpoint failures, provider failures and harness/capture failures. A `DIFFERENT` result is an observation, not a failing test by itself. `ERROR` means the case or harness could not complete; never convert missing evidence into a match. A `SKIPPED` case explains why and remains visible in the report.

## Initial cases

Use small, checked-in, synthetic fixtures; use `tests/DocumentUploadParity/fixtures/` and verify the selected fixture succeeds in both processors. Each case gets the 1–2 line explanation in the report.

1. **Simple DOCX or EML**: baseline original, section artifacts, chunk rows and vector writes for one document.
2. **Multi-section document**: section/chunk boundaries, IDs, text and row-to-vector relationships.
3. **Document with embedded image**: image/section storage and chunk image references; report unsupported or missing behavior rather than hiding it.
4. **TXT upload**: contrast C# processing with Python's upload acceptance followed by the current processor's unsupported-format result, including any storage operation before failure.
5. **Filename normalization**: spaces, case and non-ASCII characters in an otherwise valid common document; show derived IDs, paths and validation differences.
6. **Injected provider failures**: separately fail original storage, chunk upsert and vector upsert at the capture boundary; show attempted operations, response, continuation/compensation and ordering on each side. For each failure verify the fake actually intercepted the intended operation; don't infer a remote side effect from an attempted request.

Use PDFs and legacy DOC conversion as later cases if deterministic extraction/dependencies can be established. Video and authenticated admin upload/duplicate preflight are out of scope for this first report.

## Implementation sequence

1. Verify actual routes, provider SDK transports, existing fixture behavior and available test seams. Document any capture limitations before choosing the fallback. Reuse the existing `WebApplicationFactory` and Python `TestClient` patterns, but retain the real processing pipeline.
2. Build isolated Python and C# runners with deterministic embedding injection, provider capture/canned responses and network-denial safeguards. Make each record the endpoint result and an ordered operation stream, with capture level for each provider. Account for Python's import-time globals and C#'s dev route/loopback gates.
3. Add a shared comparison/report generator and fixtures. Make capture errors and unsupported formats visible even when one endpoint returns HTTP 200 with an embedded ingestion failure. Generate the single report from both runners' results.
4. Add harness tests: a known match and known field mismatch; missing versus null; random-ID reference consistency; extra/missing operations and batch/order changes; binary hashes; provider failure injection; secret redaction; network denial; deterministic regeneration. Verify all cases appear and details are not truncated.
5. Run the suite twice offline and confirm stable normalized comparisons. Record exact commands, required dependencies, fixture/model configuration, revision and any skips in the report. Review observed differences without changing application code to force parity.

## Acceptance criteria

- One reproducible report contains all cases, short descriptions, both sides' outcomes, all observed operations and inspectable sanitized details. Differences and errors are visible, including when one side produces no provider write.
- No real provider writes or credentials are needed. Unexpected outbound traffic fails closed; tests prove this.
- Each provider's capture level is stated honestly. Wire-level claims require capture after the real adapter/SDK serialization; application-level fallback is clearly limited.
- The report distinguishes structural comparisons using fake embeddings from optional real-model numeric parity. Existing differences do not fail the suite merely for being differences; harness regressions, missing evidence, unexpected network attempts and malformed reports do fail it.

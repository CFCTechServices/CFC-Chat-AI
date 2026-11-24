# Add .NET document upload and deletion, and close DOCX image and filename parity gaps

**Base:** `migration/dotnet` ← **Head:** `migration/document-parity` (8 commits)

## Summary

This PR adds admin document upload and deletion to the .NET migration, with corresponding updates to the existing web UI. It also addresses two gaps found by comparing the Python and .NET ingestion paths: Unicode filenames now produce compatible document IDs, and supported embedded DOCX images can appear in chat. No database schema migration or backfill is required.

## What changed

- **Upload and delete:** Add `POST /api/files/upload` and `DELETE /api/admin/documents/{docId}` for active admins and superusers when Supabase and Pinecone are configured. Upload validates the multipart file and checks for an existing normalized ID. Deletion removes vectors, the document's Storage tree, then chunk rows. The admin UI sends bearer tokens, reports success only after a confirmed response, and no longer offers unsupported replacement or video upload.
- **Local operator routes:** Add `POST /api/ingest/dev` and `DELETE /api/ingest/dev/{docId}`. They are unauthenticated, require a direct loopback connection, and are independently opt-in in Development (`Api:EnableDevIngest` and `Api:EnableDevDelete`). The upload route bypasses duplicate checks. Keep both flags off on shared or production hosts and never proxy these routes.
- **Unicode filenames:** Preserve the original filename in metadata and responses while using a normalized Unicode document ID and ASCII-safe Supabase object keys. Existing ASCII paths are unchanged; download and deletion also check the legacy literal path for Unicode IDs.
- **DOCX images in chat:** Extract supported embedded PNG, JPEG, and GIF images, store them in Supabase, and persist their object keys in chunk `image_paths`. Search still indexes text only. Chat resolves image paths to short-lived signed URLs through an authenticated endpoint, including when history is reopened. Failed ingestion attempts to clean up images, vectors, and chunk rows.
- **Parity and docs:** Add a synthetic Python/.NET comparison harness, fixture-based regression tests, and setup and recovery notes. Python section JSON artifacts are intentionally not reproduced. .NET keeps TXT support and its own response and error contracts.

## Verification

- `dotnet test CFC.ChatAI.slnx --no-restore` — **225 passed, 3 skipped** (fixture-dependent document parity tests), 0 failed.
- The [recorded offline verification](../complete/document-upload-remediation/README.md#verification-and-rollout) reports **23 passing Python parity tests**. The [comparison report](../tests/document-upload-parity-report.md) confirms that both implementations accept `Café Guide V2.EML` as `café-guide-v2`, store identical PNG bytes for the embedded-image fixture, and associate the chunk with the correct image key. Provider responses are canned, .NET Pinecone writes are captured before the SDK, and real-model parity was not tested. See the [parity decisions](../tests/document-upload-parity-decisions.md) for intentional differences.
- A prior [live deletion smoke test](../complete/document-deletion/document-deletion-changes.md#verification-and-outstanding-acceptance) confirmed removal of the Supabase chunk row and file; Pinecone vector removal was not independently verified.

## Before cutover

- [ ] Verify frontend routing to .NET; the .NET host does not serve the UI, and no proxy or CORS setup has been verified. The updated admin flows expect the .NET response contracts.
- [ ] With approved non-production providers, test authenticated upload, duplicate rejection, and signed download using a uniquely named Unicode DOCX with an embedded image. Verify the image in a new chat answer and reopened history, signed-link expiry, and rejection of unauthenticated requests. Delete the test document and independently confirm cleanup of originals, image objects, rows, and vectors.

Upload and deletion are not transactional. Duplicate preflight is not a lock, and failed, canceled, or ambiguous requests may require reconciliation across Supabase and Pinecone. Avoid concurrent operations on the same document ID and verify cleanup before retrying or reusing it.

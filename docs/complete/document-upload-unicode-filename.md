# Unicode document upload filenames

**Status:** Implemented and verified offline. No deployed provider was contacted, and no existing IDs or objects were migrated. DOCX image remediation remains separate.

## Behavior

- Uploading `Café Guide V2.EML` produces `café-guide-v2`, matching the tested Python ID. The source filename is preserved in the upload response and chunk metadata. The document ID, not the storage-safe encoding of it, is used for chunk rows, vector metadata, authenticated duplicate lookup, and admin download and delete routes.
- New filename-derived IDs use Unicode NFC and invariant lowercase. Letters, decimal digits, combining marks and connector punctuation are retained. Runs of other characters become one hyphen between retained characters; an empty slug falls back to `document`. IDs over 128 UTF-16 code units are rejected rather than truncated. Existing ASCII IDs, including IDs containing periods other than `.` and `..`, remain valid for lookup.
- Download and delete use the exact ID supplied by ASP.NET routing, with no second URL decode, case folding or normalization. This keeps separately stored spellings addressable. The authenticated upload preflight detects an existing canonical ID (including collisions between new case and normalization variants), but does not scan for aliases of legacy IDs. An older noncanonical ID can coexist with a new canonical one; any inventory or backfill is a separate operation. Duplicate preflight is not atomic, and the loopback development upload still skips it.
- Uploads still reject traversal, separators, control characters, reserved Windows filenames and overlong filenames. The API also rejects literal `%HH` escapes in multipart filenames, including encoded and double-encoded separators; it does not URL-decode the filename. IDs reject percent escapes, separators and control characters after routing.

## Storage layout

[Upstream Supabase Storage's object-key validator](https://github.com/supabase/storage/blob/645343a4be3d5517134f689327a9a1df34d80a91/src/storage/limits.ts) allows only a subset of ASCII. The older parity run only captured Python's *attempt* to write a literal Unicode key; its canned HTTP 200 was not proof that Storage accepted it.

- ASCII document keys and video keys are unchanged. For non-ASCII document IDs, the new root is `docs/!u-{base64url(UTF-8 document ID)}`. The encoding is reversible, and `!` cannot occur in a literal document ID, so the two layouts cannot collide. For the fixture, the original is `docs/!u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml`. The ASCII basename keeps the storage key short and is the filename returned by the signed-download endpoint for this layout.
- Download checks the encoded root first, then the literal `docs/{id}/original` prefix for earlier imports. Delete checks both roots for that exact ID. The fallback does not rewrite existing keys; actually signing or deleting a literal Unicode object still depends on what the deployed provider supports.

## Verification

- `dotnet test CFC.ChatAI.slnx --no-restore`: **197 passed, 3 skipped**. Tests cover both multipart filename encodings used by clients, NFC and case collisions, non-Latin IDs, invalid names and route values, authenticated duplicate checks, signed-download routing, chunk/vector metadata, provider query escaping and deletion of encoded and legacy objects without touching a neighboring ID.
- The Python parity harness suite passed **22 tests**. Two runs of the [isolated parity harness](../tests/document-upload-parity-report.md#filename-normalization) produced identical normalized comparisons. Both endpoints returned HTTP 200 and reported `café-guide-v2`; the harness asserts the C# storage request uses the ASCII-safe key. Chunk rows and deterministic vectors matched after generated ID aliasing. Storage layout, section artifacts, response contracts and operation order still differ intentionally.
- Before rollout, smoke-test authenticated upload, duplicate detection, signed download and deletion against an isolated instance of the target Supabase deployment. If legacy Unicode objects are known to exist, also verify the literal-path fallback there. Offline test doubles and request captures do not prove provider-side behavior.

# Plan: Repair MIME metadata for legacy document images

**Status:** Proposed. The immediate cause of the reported 404 is confirmed; the Python uploader hypothesis still needs a regression test.

## Problem

In the [local .NET development setup](../../deployment/DOTNET-IIS.md#local-development), a chat reply showed `Untitled (unavailable)` for an image from the Concept 5 Getting Started Guide. The API successfully listed `cfc-docs` but returned 404 without requesting a signed URL. Supabase Storage showed the exact object as `text/plain`:

```text
cfc-docs / docs/concept-5-getting-started-guide/images/concept-5-getting-started-guide__img001.jpg
```

The [.NET image resolver](../../dotnet/src/CFC.ChatAI.Api/Endpoints/DocumentImageEndpoints.cs) requires the stored MIME type to match the file extension (`image/jpeg` for `.jpg`). The [old Python endpoint](../../app/api/endpoints/chat.py) inferred MIME type from the extension and streamed the bytes, masking incorrect metadata. The likely source is Python [`store_image()`](../../app/services/supabase_content_repository.py), which supplies `contentType` instead of the Storage SDK's `content-type` option. Confirm this with the SDK version used by the uploader.

The image was not found in current `document_chunks.image_paths`. That does not cause the 404: the resolver does not read chunk rows. A reopened chat can use a saved image reference, and a fresh reply can fall back to Pinecone metadata. Investigate missing chunk links separately only if fresh replies cannot retrieve the intended images.

## Remediation

1. **Confirm and prevent new bad uploads.** Reproduce the Python upload with a small JPEG fixture and inspect the outgoing `Content-Type` and the stored `metadata.mimetype` in an isolated development bucket. If confirmed, change `store_image()` to use `content-type`. Add a regression test that exercises the installed SDK's request construction rather than only mocking `store_image()`. Check JPEG, PNG, and GIF. Audit other uses of `contentType` separately; do not change unrelated upload flows without verifying their behavior.
2. **Repair the reported object first.** Download a private backup of the original bytes and record its current metadata. Verify that the bytes decode as JPEG. Update the content type to `image/jpeg` at the same bucket and object key, or overwrite with the same verified bytes and an explicit `Content-Type` if the available tooling cannot update metadata alone. Do not change bucket visibility, rename the object, or re-ingest the document for this repair. Verify the stored MIME type and that the bytes did not change.
3. **Verify end to end.** In a signed-in browser, reopen the affected chat. The image resolver should return `200` with signed-link JSON; the subsequent Storage request should return `200` with `image/jpeg`, and the image should render. Confirm the resolver still rejects unsupported or mismatched MIME types. Do not include signed URLs or credentials in test records.
4. **Inventory before any wider repair.** List objects under the development documents bucket's `docs/*/images/` paths whose stored MIME type does not match a supported extension. Produce a dry-run report of object keys, current MIME types, and expected types. Repair only approved objects whose bytes have been verified, preserving keys and access controls. Skip and report corrupt, unsupported, or ambiguous objects. Keep a record of changes and a way to restore original bytes and metadata if needed.

## Done when

- The reported image renders in the reopened chat without a C# validation change or a chunk edit.
- If the Python upload path is still in use, a regression test and an isolated Storage smoke test show that new images are stored with the correct MIME type.
- Any wider cleanup is reviewed separately and records repaired and skipped objects.

# Document upload parity findings and decisions

**Status: Unicode filename fix implemented and verified offline; DOCX image support remains planned.** This note interprets the synthetic results in [document-upload-parity-report.md](document-upload-parity-report.md). The implementation plan is [document upload image and filename remediation](../plan/document-upload-image-filename-remediation.md).

## Three principal gaps

### 1. Embedded document images — plan to fix

In the embedded-image DOCX fixture, both endpoints returned HTTP 200, and the text embedding input and captured vector matched after ID aliasing. Python attempted to store an extracted image and put its storage path in the chunk's `image_paths`; C# did neither. This is a functional gap, not just a different wire format: retrieval and chat read image paths from chunk data or vector metadata, so the tested C# ingestion does not provide this image through that mechanism.

The fixture establishes the gap for one DOCX image; it does not establish image behavior for every document format or image layout. The plan starts with DOCX and explicitly tests the association between images and chunks rather than assuming that all images belong to all chunks. These are document images, not embedding vectors.

### 2. Filename / document ID compatibility — fixed offline

In the initial run, Python accepted `Café Guide V2.EML` and derived `café-guide-v2`; the C# development endpoint returned 400 because `DocumentId.IsValid` rejected non-ASCII IDs. The regenerated report now shows both endpoints accepting the file with the same ID. New C# uploads use an ASCII-safe original object key because upstream Supabase Storage rejects literal Unicode keys. See the [completion note](../complete/document-upload-unicode-filename.md) for the lifecycle policy, tests, and live verification still needed.

### 3. Section JSON artifacts — do not plan to reproduce

Python attempted to upload structured section JSON under `docs/{doc_id}/sections/{section_id}.json`, including title, heading level and ordered text/image blocks. C# did not. This does **not** mean C# failed to identify sections: for the simple EML and multi-section DOCX fixtures, its chunk rows and vectors matched Python's after generated IDs were aliased.

The inspected Python ingestion code assigns section paths to in-memory chunks, but does not write `section_path` into the captured chunk rows or vector metadata. Retrieval and chat models have a section-path field, but no direct section-JSON reader was identified in the application code inspected. We do not currently plan to create these artifacts; that is a scope decision, not proof they are unused. Revisit if an external consumer, restore/reprocessing workflow, or product requirement depends on them. Image storage and chunk `image_paths` remain in scope independently.

## Other observed differences

- **Ordering and failure handling:** Python attempts original storage before chunk/vector writes; C# writes chunks and vectors before the original and attempts compensating deletes on failure. Prefer retaining explicit C# error statuses and compensation rather than copying Python's HTTP 200 with failure in the body. This is a direction, not evidence that rollback always succeeds; lost responses, cleanup failures, overwrites and concurrency still need testing.
- **Response shape and endpoint:** Success bodies differ. The report compares Python's upload route with C#'s development-only route, which skips the authenticated route's duplicate check. Do not infer full production API compatibility from this report; review client contract and authenticated-route tests separately.
- **TXT:** C# ingests the tested TXT file, while Python stores its original but reports unsupported ingestion. Retain C# support unless product requirements say otherwise; broader support is not inherently a defect.
- **Supabase request details:** Raw versus multipart upload, headers, cache control and upsert parameters differ. Do not chase byte-for-byte SDK requests merely for parity; confirm required storage options and actual upsert/overwrite semantics against the target service before rollout. The synthetic test doesn't establish equivalent remote effects.
- **Pinecone:** Python SDK requests were captured at transport level; C# was captured at an application seam. No conclusion about C# SDK serialization, namespace, production batching or actual remote writes follows from a missing HTTP capture. Real-model parity was skipped; deterministic test vectors do not establish it.
- **Counts:** The report's `DIFFERENT` counts include wire-format differences and observability gaps as well as behavior; they are not counts of defects or severity scores. All provider responses in the investigation were canned, and compensation records are attempts only.

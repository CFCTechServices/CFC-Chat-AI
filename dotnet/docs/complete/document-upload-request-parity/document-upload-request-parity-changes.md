# PR: document upload request parity investigation

## Summary

- Added a reproducible, synthetic upload comparison and the [single parity report](../../tests/document-upload-parity-report.md). Moved the [completed plan](document-upload-request-parity.md) into this folder.
- Exercised the real Python `/api/files/upload` and C# `/api/ingest/dev` endpoint, processor and ingestion/handler paths with the same EML, multi-section DOCX, image DOCX, TXT and non-ASCII filename fixtures. Original-storage, chunk-upsert and vector-upsert failures are injected separately.
- Captured serialized Supabase requests on both sides and Python Pinecone discovery/upserts at the HTTP transport. C# Pinecone uses the existing internal embedding/vector-store seam to record deterministic vectors; the only production assembly change grants the test runner internal visibility and does not change behavior.
- Added reference validation, structured comparisons, redaction, network-denial and failure-injection checks, full sanitized payload details, binary hashes, pinned harness dependencies and report-regeneration tests. The report is overwritten on each run and the runner checks two offline runs for stable normalized comparisons.

## Observed results

- Baseline and multi-section chunk rows and deterministic test vectors match after ID validation/aliasing, but responses, provider order and storage requests differ. Python writes section artifacts; C# does not capture section writes.
- Python records an image artifact and chunk image reference; C# records neither. In the tested Python SDK version, the image request has a `contenttype: image/png` header while its multipart file part is `text/plain; charset="UTF-8"`; this is transport evidence, not a claim about remote storage behavior.
- Python accepts TXT at upload, stores the original and returns HTTP 200 with an embedded unsupported-format ingestion failure; C# processes the TXT. The non-ASCII filename succeeds in Python but is rejected with HTTP 400 before C# provider operations.
- On injected original-storage failure Python continues ingestion; C# returns 503 and attempts vector/chunk compensation. Chunk and vector failures are likewise surfaced differently, with C# compensation attempts shown in the report. These are attempted operations against canned responses, not proof of remote side effects.

## Safety and limitations

The Python runner strips inherited provider settings, blocks dotenv reads and socket/DNS traffic, and intercepts SDK HTTP; C# runs in an in-process TestServer inside a `--network none` container with no repository or `.env` mount. Both self-test denial of unhandled traffic. No real credentials or provider writes are used.

C# Pinecone SDK serialization, batching, headers and actual namespace are **not observable** at the substituted vector-store boundary. The optional real-model comparison is **skipped**; test vectors do not establish numerical parity of real embeddings. See the report for capture levels, SDK discovery, fixture hashes, complete payloads and exact offline setup commands.

## Verification

- Python harness and fixture tests: **22 passed**.
- C# processor fixture tests: **4 passed**.
- Eight upload cases ran twice offline with stable normalized comparisons; all were `DIFFERENT`, and the optional real-model case was `SKIPPED`. Differences themselves do not fail the suite.

```bash
python -m unittest discover -s dotnet/tests/DocumentUploadParity -p 'test_*.py'
dotnet test dotnet/tests/CFC.ChatAI.DocumentProcessor.Tests/CFC.ChatAI.DocumentProcessor.Tests.csproj --no-restore --filter FullyQualifiedName~UploadParityFixtureTests
python dotnet/tests/DocumentUploadParity/run_parity.py
```

# Document upload request parity report

Synthetic, in-process upload investigation. `DIFFERENT` is an observation, not a failing test. A canned response records an **attempt**, not a remote side effect. No real provider writes occur.

| Case | Outcome | Operations Python / C# | Differences |
| --- | --- | ---: | ---: |
| Baseline EML | DIFFERENT | 6 / 3 | 33 |
| Multi-section DOCX | DIFFERENT | 7 / 3 | 34 |
| Embedded image DOCX | DIFFERENT | 7 / 3 | 37 |
| TXT upload | DIFFERENT | 1 / 3 | 23 |
| Filename normalization | DIFFERENT | 6 / 0 | 24 |
| Injected original storage failure | DIFFERENT | 6 / 5 | 34 |
| Injected chunk upsert failure | DIFFERENT | 3 / 2 | 22 |
| Injected vector upsert failure | DIFFERENT | 6 / 4 | 30 |
| Optional real model | SKIPPED | 0 / 0 | not compared |

## Legend and capture limits

- Python `POST /api/files/upload` and C# `POST /api/ingest/dev` use real endpoint, processor and ingestion/handler paths. The latter is Development-only, opt-in, loopback-only and skips the authenticated route's duplicate check.
- Supabase on both sides and Python Pinecone are **transport-level**: real adapters/SDKs serialize request bodies before the test transport captures them. The handler observes HTTP requests before final socket framing; no actual HTTP transmission is claimed. Python Storage uses multipart; its parts below are parsed from the serialized bytes. Binary bytes are represented by size and SHA-256, not reproduced.
- C# Pinecone is **application-level**, not SDK HTTP JSON: it records `VectorRecord`s at the internal vector-store seam. C# SDK serialization, HTTP headers, SDK batching and remote effects are not proven. The seam does not expose the namespace; `parity-namespace` is configured on both sides but only Python's serialized value is observable. A missing C# Pinecone HTTP capture means the SDK was bypassed, not that production would send no request.
- SDK reconnaissance: Python `VectorStore` lists indexes and may create one before resolving `Index(name)`; SDK 10.0.0 uses HTTP for discovery and upserts. List and describe are captured and answered with canned responses pointing to a synthetic host; unexpected creation/discovery is blocked. C# SDK `Pinecone.Client` 4.0.2 exposes `ClientOptions.HttpClient` for control-plane HTTP and `GrpcOptions` for data-plane gRPC, but the production wrapper constructs its own client without injected options. A safe serialized gRPC/discovery interception was not established, so the existing internal vector seam is used instead.
- Failure injection returns canned HTTP 503 for Supabase and non-retryable HTTP 400 for Python Pinecone, so an SDK retry cannot turn the injected vector failure into a later success. The C# vector seam throws a test exception. Each requested hook is verified against the recorded attempted operation; compensation is also only an attempt.
- The four-dimensional vectors are deterministic **test values**, not model embeddings: UTF-8 byte length, byte sum modulo 997, position-weighted byte sum modulo 997, and ASCII-space count. Inputs and all values appear in the captures; equality is exact.
- Before per-side section/chunk aliases are assigned, section-to-row and row-to-vector references are validated where observable. C# emits no section artifacts, so its section IDs are first observed in rows and empty sections are unobservable. Different chunk boundaries are displayed as unmatched ordered lists. Document text, paths, filenames, field presence, types, request order and batches are not normalized. Multipart boundary strings are normalized only in comparisons; original IDs and boundaries remain in details. Object key order is ignored; array order is retained. `<MISSING>` and `null` are distinct; `<NOT OBSERVABLE>` denotes the C# SDK capture limitation, not an absent request. Difference counts include these coverage gaps.
- Each Python process removes inherited credentials, disables dotenv before imports, blocks socket/DNS calls and intercepts unexpected HTTP. On Windows, its event loop's private `socketpair` alone is allowed to use loopback. The C# runner runs in a Docker container with `--network none`, only loopback, no repository mount, and a read-only root. Both deliberately test an unhandled request and outbound denial. Unknown traffic fails the harness.

## Baseline EML

One small email exercises the original, section, chunk and vector paths. Fixture: `baseline.eml` as `baseline.eml`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 200. Section/image storage attempts: Python 1/0, C# 0/0. Chunk rows and test vectors match after ID validation and aliasing.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | chunks-upsert → vectors-upsert → storage-original |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: MATCH; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: MATCH; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.response.chunk_count | `<MISSING>` | `1` |
| endpoint.response.document_id | `<MISSING>` | `"baseline"` |
| endpoint.response.ingestion | `{"chunks_processed":1,"doc_id":"baseline","error":null,"images_processed":0,"message":"Successfully ingested baseline.eml","sections_processed":1,"source":"<documents>\\baseline.eml","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.source_name | `<MISSING>` | `"baseline.eml"` |
| endpoint.response.source_type | `<MISSING>` | `"document"` |
| endpoint.response.supabase | `{"path":"docs/baseline/original/baseline.eml","stored":true}` | `<MISSING>` |
| endpoint.response.vector_count | `<MISSING>` | `1` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[2].kind | `"chunks-upsert"` | `"storage-original"` |
| ordered_operations[3] | `{"kind":"index-list","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[4] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"512"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"497"` | `"222"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"message/rfc822"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"835","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"258","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"baseline","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/baseline/sections/<section-1>.json","suggested_name":"baseline__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162.0,
        644.0,
        407.0,
        17.0
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
              "size": 222
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"baseline.eml\"",
              "content-type": "message/rfc822"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "497",
        "content-type": "multipart/form-data; boundary=86b6623ba43cb24443494149dce3943c",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"2ace8fba-6fbb-417a-958c-65ca2684568d.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
                  "type": "text"
                }
              ],
              "doc_slug": "baseline",
              "index": 1,
              "level": 1,
              "section_id": "2ace8fba-6fbb-417a-958c-65ca2684568d",
              "storage_path": "docs/baseline/sections/2ace8fba-6fbb-417a-958c-65ca2684568d.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "c3fb3a3477de25c05c2f997ee037057043a43d31d26758baf88d6186d81e6865",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=2a2d06301d4b58bd65ea3792151ec64c",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/2ace8fba-6fbb-417a-958c-65ca2684568d.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "a7b568a8-791b-45ed-a2c5-7961e2aa4872",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "2ace8fba-6fbb-417a-958c-65ca2684568d",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "512",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "a7b568a8-791b-45ed-a2c5-7961e2aa4872",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "2ace8fba-6fbb-417a-958c-65ca2684568d",
              "source": "baseline.eml",
              "source_type": "document"
            },
            "values": [
              162.0,
              644.0,
              407.0,
              17.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "258",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "chunks_processed": 1,
      "doc_id": "baseline",
      "error": null,
      "images_processed": 0,
      "message": "Successfully ingested baseline.eml",
      "sections_processed": 1,
      "source": "<documents>\\baseline.eml",
      "success": true
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/baseline/original/baseline.eml",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162,
        644,
        407,
        17
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "9bacd602-038a-4633-8572-1ba2c0509165",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "a7641903-5db4-4f9a-91cf-84f309be6db2",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "canned-success",
      "vectors": [
        {
          "id": "9bacd602-038a-4633-8572-1ba2c0509165",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "a7641903-5db4-4f9a-91cf-84f309be6db2",
            "source": "baseline.eml",
            "source_type": "document"
          },
          "values": [
            162,
            644,
            407,
            17
          ]
        }
      ]
    },
    {
      "body": {
        "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
        "size": 222
      },
      "capture": "wire",
      "headers": {
        "content-length": "222",
        "content-type": "message/rfc822",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "chunk_count": 1,
    "document_id": "baseline",
    "source_name": "baseline.eml",
    "source_type": "document",
    "vector_count": 1
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_requests": 0
}
```
</details>

## Multi-section DOCX

Two headings exercise section boundaries, chunk order and references. Fixture: `multi-section.docx` as `multi-section.docx`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 200. Section/image storage attempts: Python 2/0, C# 0/0. Chunk rows and test vectors match after ID validation and aliasing.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original → storage-section → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | chunks-upsert → vectors-upsert → storage-original |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198","size":35339},"object_content_type":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","path":"/storage/v1/object/parity-bucket/docs/multi-section/original/multi-section.docx","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198","size":35339},"object_content_type":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","path":"/storage/v1/object/parity-bucket/docs/multi-section/original/multi-section.docx","x-upsert":"true"}` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: MATCH; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: MATCH; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.response.chunk_count | `<MISSING>` | `2` |
| endpoint.response.document_id | `<MISSING>` | `"multi-section"` |
| endpoint.response.ingestion | `{"chunks_processed":2,"doc_id":"multi-section","error":null,"images_processed":0,"message":"Successfully ingested multi-section.docx","sections_processed":2,"source":"<documents>\\multi-section.docx","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.source_name | `<MISSING>` | `"multi-section.docx"` |
| endpoint.response.source_type | `<MISSING>` | `"document"` |
| endpoint.response.supabase | `{"path":"docs/multi-section/original/multi-section.docx","stored":true}` | `<MISSING>` |
| endpoint.response.vector_count | `<MISSING>` | `2` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[2].kind | `"storage-section"` | `"storage-original"` |
| ordered_operations[3] | `{"kind":"chunks-upsert","provider":"supabase","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[4] | `{"kind":"index-list","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[6] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"735"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"35677"` | `"35339"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"application/vnd.openxmlformats-officedocument.wordprocessingml.document"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"698","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/multi-section/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"},{"headers":{"accept":"*/*","content-length":"697","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/multi-section/sections/<section-2>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"490","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Synthetic first section.","type":"text"}],"doc_slug":"multi-section","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/multi-section/sections/<section-1>.json","suggested_name":"multi-section__sec001_overview.json","title":"Overview"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/multi-section/sections/<section-1>.json","x-upsert":"true"},{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Synthetic second section.","type":"text"}],"doc_slug":"multi-section","index":2,"level":2,"section_id":"<section-2>","storage_path":"docs/multi-section/sections/<section-2>.json","suggested_name":"multi-section__sec002_details.json","title":"Details"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/multi-section/sections/<section-2>.json","x-upsert":"true"}]` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Synthetic first section.",
      "values": [
        24.0,
        380.0,
        176.0,
        2.0
      ]
    },
    {
      "input": "Synthetic second section.",
      "values": [
        25.0,
        464.0,
        371.0,
        2.0
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198",
              "size": 35339
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"multi-section.docx\"",
              "content-type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "35677",
        "content-type": "multipart/form-data; boundary=9c25e96a346b7f185523ac8cc79fc02e",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/original/multi-section.docx",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"d5b293ab-695d-4a89-8ab7-53e78af1925b.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Synthetic first section.",
                  "type": "text"
                }
              ],
              "doc_slug": "multi-section",
              "index": 1,
              "level": 1,
              "section_id": "d5b293ab-695d-4a89-8ab7-53e78af1925b",
              "storage_path": "docs/multi-section/sections/d5b293ab-695d-4a89-8ab7-53e78af1925b.json",
              "suggested_name": "multi-section__sec001_overview.json",
              "title": "Overview"
            },
            "sha256": "387383c375d53819b3f5412ecee3a3a5f7b34e73db46e4bf5279caf791a464ad",
            "size": 384
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "698",
        "content-type": "multipart/form-data; boundary=34b2843893860631f34a62850541c86e",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/d5b293ab-695d-4a89-8ab7-53e78af1925b.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"012d881a-be04-46e3-8ecc-86abc3a4ee1b.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Synthetic second section.",
                  "type": "text"
                }
              ],
              "doc_slug": "multi-section",
              "index": 2,
              "level": 2,
              "section_id": "012d881a-be04-46e3-8ecc-86abc3a4ee1b",
              "storage_path": "docs/multi-section/sections/012d881a-be04-46e3-8ecc-86abc3a4ee1b.json",
              "suggested_name": "multi-section__sec002_details.json",
              "title": "Details"
            },
            "sha256": "255a607f98c8e5593d6a8a27b78f8c3c22864fb91644c0a90ade1038e05cd011",
            "size": 383
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "697",
        "content-type": "multipart/form-data; boundary=9c77e40f040687c551c826f5300d6bfa",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/012d881a-be04-46e3-8ecc-86abc3a4ee1b.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "46136e39-e2c4-45bf-97ff-416c4fbada95",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "d5b293ab-695d-4a89-8ab7-53e78af1925b",
          "section_title": "Overview",
          "source": "multi-section.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        },
        {
          "chunk_id": "89633320-7283-4825-936d-570860896212",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "012d881a-be04-46e3-8ecc-86abc3a4ee1b",
          "section_title": "Details",
          "source": "multi-section.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "735",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "46136e39-e2c4-45bf-97ff-416c4fbada95",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "d5b293ab-695d-4a89-8ab7-53e78af1925b",
              "source": "multi-section.docx",
              "source_type": "document"
            },
            "values": [
              24.0,
              380.0,
              176.0,
              2.0
            ]
          },
          {
            "id": "89633320-7283-4825-936d-570860896212",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "012d881a-be04-46e3-8ecc-86abc3a4ee1b",
              "source": "multi-section.docx",
              "source_type": "document"
            },
            "values": [
              25.0,
              464.0,
              371.0,
              2.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "490",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "chunks_processed": 2,
      "doc_id": "multi-section",
      "error": null,
      "images_processed": 0,
      "message": "Successfully ingested multi-section.docx",
      "sections_processed": 2,
      "source": "<documents>\\multi-section.docx",
      "success": true
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/multi-section/original/multi-section.docx",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Synthetic first section.",
      "values": [
        24,
        380,
        176,
        2
      ]
    },
    {
      "input": "Synthetic second section.",
      "values": [
        25,
        464,
        371,
        2
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "1220e8ca-249f-4373-bea9-88413d7355a6",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "12f9d7e7-a068-4741-86a3-9321a6d097cd",
          "section_title": "Overview",
          "source": "multi-section.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        },
        {
          "chunk_id": "6a6b8b68-c968-428c-b8fc-fd3fc0df1b5f",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "2059245e-d55a-4a53-853c-cb8128c5bd35",
          "section_title": "Details",
          "source": "multi-section.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "canned-success",
      "vectors": [
        {
          "id": "1220e8ca-249f-4373-bea9-88413d7355a6",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "12f9d7e7-a068-4741-86a3-9321a6d097cd",
            "source": "multi-section.docx",
            "source_type": "document"
          },
          "values": [
            24,
            380,
            176,
            2
          ]
        },
        {
          "id": "6a6b8b68-c968-428c-b8fc-fd3fc0df1b5f",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "2059245e-d55a-4a53-853c-cb8128c5bd35",
            "source": "multi-section.docx",
            "source_type": "document"
          },
          "values": [
            25,
            464,
            371,
            2
          ]
        }
      ]
    },
    {
      "body": {
        "sha256": "87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198",
        "size": 35339
      },
      "capture": "wire",
      "headers": {
        "content-length": "35339",
        "content-type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/original/multi-section.docx",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "chunk_count": 2,
    "document_id": "multi-section",
    "source_name": "multi-section.docx",
    "source_type": "document",
    "vector_count": 2
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_requests": 0
}
```
</details>

## Embedded image DOCX

A one-pixel image exercises image artifacts and chunk image references. Fixture: `embedded-image.docx` as `embedded-image.docx`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 200. Section/image storage attempts: Python 1/1, C# 0/0.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original → storage-image → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | chunks-upsert → vectors-upsert → storage-original |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929","size":35800},"object_content_type":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","path":"/storage/v1/object/parity-bucket/docs/embedded-image/original/embedded-image.docx","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929","size":35800},"object_content_type":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","path":"/storage/v1/object/parity-bucket/docs/embedded-image/original/embedded-image.docx","x-upsert":"true"}` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: MATCH; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: DIFFERENT; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.response.chunk_count | `<MISSING>` | `1` |
| endpoint.response.document_id | `<MISSING>` | `"embedded-image"` |
| endpoint.response.ingestion | `{"chunks_processed":1,"doc_id":"embedded-image","error":null,"images_processed":1,"message":"Successfully ingested embedded-image.docx","sections_processed":1,"source":"<documents>\\embedded-image.docx","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.source_name | `<MISSING>` | `"embedded-image.docx"` |
| endpoint.response.source_type | `<MISSING>` | `"document"` |
| endpoint.response.supabase | `{"path":"docs/embedded-image/original/embedded-image.docx","stored":true}` | `<MISSING>` |
| endpoint.response.vector_count | `<MISSING>` | `1` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1].kind | `"storage-image"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[2].kind | `"storage-section"` | `"storage-original"` |
| ordered_operations[3] | `{"kind":"chunks-upsert","provider":"supabase","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[4] | `{"kind":"index-list","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[6] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"469"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-image | `[{"headers":{"accept":"*/*","content-length":"367","content-type":"multipart/form-data; boundary=<boundary>","contenttype":"image/png","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/embedded-image/images/embedded-image__img001.png","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"36139"` | `"35800"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"application/vnd.openxmlformats-officedocument.wordprocessingml.document"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"985","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/embedded-image/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"268","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-image | `[{"kind":"storage-image","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"f688dd6e21f010dd1052e979d9bd19c3a054ca0266e15e5e5673f97e3f7f9271","size":68},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/embedded-image/images/embedded-image__img001.png","x-upsert":"true"}]` | `<MISSING>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Synthetic text before the image.","type":"text"},{"path":"docs/embedded-image/images/embedded-image__img001.png","storage_path":"docs/embedded-image/images/embedded-image__img001.png","type":"image"},{"text":"Synthetic text after the image.","type":"text"}],"doc_slug":"embedded-image","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/embedded-image/sections/<section-1>.json","suggested_name":"embedded-image__sec001_illustration.json","title":"Illustration"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/embedded-image/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |
| chunk_rows[0].image_paths[0] | `"docs/embedded-image/images/embedded-image__img001.png"` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Synthetic text before the image. Synthetic text after the image.",
      "values": [
        64.0,
        43.0,
        930.0,
        9.0
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929",
              "size": 35800
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"embedded-image.docx\"",
              "content-type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "36139",
        "content-type": "multipart/form-data; boundary=7cdfbf87c3326bd0e27d6d7879f72486",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/original/embedded-image.docx",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "f688dd6e21f010dd1052e979d9bd19c3a054ca0266e15e5e5673f97e3f7f9271",
              "size": 68
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"embedded-image__img001.png\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "367",
        "content-type": "multipart/form-data; boundary=26f9930ba928485907bd55b9676234fb",
        "contenttype": "image/png",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-image",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/images/embedded-image__img001.png",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"9c1755c1-6a0c-4842-96e0-016176a0d807.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Synthetic text before the image.",
                  "type": "text"
                },
                {
                  "path": "docs/embedded-image/images/embedded-image__img001.png",
                  "storage_path": "docs/embedded-image/images/embedded-image__img001.png",
                  "type": "image"
                },
                {
                  "text": "Synthetic text after the image.",
                  "type": "text"
                }
              ],
              "doc_slug": "embedded-image",
              "index": 1,
              "level": 1,
              "section_id": "9c1755c1-6a0c-4842-96e0-016176a0d807",
              "storage_path": "docs/embedded-image/sections/9c1755c1-6a0c-4842-96e0-016176a0d807.json",
              "suggested_name": "embedded-image__sec001_illustration.json",
              "title": "Illustration"
            },
            "sha256": "7b2112e894caba180a383740f002512d0fdcd79e1f8f04647c360c3d90fc944d",
            "size": 671
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "985",
        "content-type": "multipart/form-data; boundary=7ece6f90d5011853f25ab983c332ac62",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/sections/9c1755c1-6a0c-4842-96e0-016176a0d807.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "dc58feab-b1be-4947-8584-91753d8f9d16",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [
            "docs/embedded-image/images/embedded-image__img001.png"
          ],
          "section_id": "9c1755c1-6a0c-4842-96e0-016176a0d807",
          "section_title": "Illustration",
          "source": "embedded-image.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "469",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "dc58feab-b1be-4947-8584-91753d8f9d16",
            "metadata": {
              "doc_id": "embedded-image",
              "section_id": "9c1755c1-6a0c-4842-96e0-016176a0d807",
              "source": "embedded-image.docx",
              "source_type": "document"
            },
            "values": [
              64.0,
              43.0,
              930.0,
              9.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "268",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "chunks_processed": 1,
      "doc_id": "embedded-image",
      "error": null,
      "images_processed": 1,
      "message": "Successfully ingested embedded-image.docx",
      "sections_processed": 1,
      "source": "<documents>\\embedded-image.docx",
      "success": true
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/embedded-image/original/embedded-image.docx",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Synthetic text before the image. Synthetic text after the image.",
      "values": [
        64,
        43,
        930,
        9
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "ae7bcb56-7033-4cc5-8f0b-23ca7a4f8de6",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "7d88f093-f911-4521-be81-4b79ea952716",
          "section_title": "Illustration",
          "source": "embedded-image.docx",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "canned-success",
      "vectors": [
        {
          "id": "ae7bcb56-7033-4cc5-8f0b-23ca7a4f8de6",
          "metadata": {
            "doc_id": "embedded-image",
            "section_id": "7d88f093-f911-4521-be81-4b79ea952716",
            "source": "embedded-image.docx",
            "source_type": "document"
          },
          "values": [
            64,
            43,
            930,
            9
          ]
        }
      ]
    },
    {
      "body": {
        "sha256": "0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929",
        "size": 35800
      },
      "capture": "wire",
      "headers": {
        "content-length": "35800",
        "content-type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/original/embedded-image.docx",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "chunk_count": 1,
    "document_id": "embedded-image",
    "source_name": "embedded-image.docx",
    "source_type": "document",
    "vector_count": 1
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_requests": 0
}
```
</details>

## TXT upload

The same plain text reaches C# processing but Python's processor does not support TXT. Fixture: `unsupported.txt` as `unsupported.txt`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success false; C# HTTP 200. Section/image storage attempts: Python 0/0, C# 0/0.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original | chunks-upsert → vectors-upsert → storage-original |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa","size":100},"object_content_type":"text/plain","path":"/storage/v1/object/parity-bucket/docs/unsupported/original/unsupported.txt","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa","size":100},"object_content_type":"text/plain","path":"/storage/v1/object/parity-bucket/docs/unsupported/original/unsupported.txt","x-upsert":"true"}` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: MATCH; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: neither side captured SDK HTTP; storage_objects: DIFFERENT; chunk_batch_sizes: DIFFERENT; vector_batch_sizes_at_capture_boundary: DIFFERENT; chunk boundaries/text differ; rows and vectors shown as unmatched ordered lists; namespace: Python did not call its index; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.response.chunk_count | `<MISSING>` | `1` |
| endpoint.response.document_id | `<MISSING>` | `"unsupported"` |
| endpoint.response.ingestion | `{"error":"400: Unsupported file type: .txt","success":false}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.source_name | `<MISSING>` | `"unsupported.txt"` |
| endpoint.response.source_type | `<MISSING>` | `"document"` |
| endpoint.response.supabase | `{"path":"docs/unsupported/original/unsupported.txt","stored":true}` | `<MISSING>` |
| endpoint.response.vector_count | `<MISSING>` | `1` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1] | `<MISSING>` | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` |
| ordered_operations[2] | `<MISSING>` | `{"kind":"storage-original","provider":"supabase","result":"canned-success"}` |
| supabase_http.chunks-upsert | `<MISSING>` | `[{"headers":{"content-type":"application/json; charset=utf-8","prefer":"resolution=merge-duplicates,return=minimal"},"method":"POST","origin":"https://provider.invalid","path":"/rest/v1/document_chunks","provider_response_status":200,"query":"?on_conflict=chunk_id","result":"canned-success"}]` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"374"` | `"100"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"text/plain"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| chunk_batch_sizes[0] | `<MISSING>` | `1` |
| vector_batch_sizes_at_capture_boundary[0] | `<MISSING>` | `1` |
| chunk_rows.unmatched_boundaries | `[]` | `[{"chunk_id":"<chunk-1>","content":"This synthetic text fixture is supported by the C# processor but not the Python document processor.","doc_id":"unsupported","end_seconds":null,"image_paths":[],"section_id":"<section-1>","section_title":"Untitled","source":"unsupported.txt","source_type":"document","srt_url":null,"start_seconds":null,"txt_url":null,"video_url":null,"vtt_url":null}]` |
| vectors.unmatched_chunks | `[]` | `[{"id":"<chunk-1>","metadata":{"doc_id":"unsupported","section_id":"<section-1>","source":"unsupported.txt","source_type":"document"},"values":[99,514,39,15]}]` |
| embedding_inputs_outputs.unmatched_chunks | `[]` | `[{"input":"This synthetic text fixture is supported by the C# processor but not the Python document processor.","values":[99,514,39,15]}]` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa",
              "size": 100
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"unsupported.txt\"",
              "content-type": "text/plain"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "374",
        "content-type": "multipart/form-data; boundary=e30b5387ded2c7299ea7a9d64c30532b",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/unsupported/original/unsupported.txt",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "error": "400: Unsupported file type: .txt",
      "success": false
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/unsupported/original/unsupported.txt",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "This synthetic text fixture is supported by the C# processor but not the Python document processor.",
      "values": [
        99,
        514,
        39,
        15
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "803ff097-e7a9-4c15-a1df-d13f2aa8dd8b",
          "content": "This synthetic text fixture is supported by the C# processor but not the Python document processor.",
          "doc_id": "unsupported",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "a1e2f892-7234-471f-a123-a8854420dcb1",
          "section_title": "Untitled",
          "source": "unsupported.txt",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "canned-success",
      "vectors": [
        {
          "id": "803ff097-e7a9-4c15-a1df-d13f2aa8dd8b",
          "metadata": {
            "doc_id": "unsupported",
            "section_id": "a1e2f892-7234-471f-a123-a8854420dcb1",
            "source": "unsupported.txt",
            "source_type": "document"
          },
          "values": [
            99,
            514,
            39,
            15
          ]
        }
      ]
    },
    {
      "body": {
        "sha256": "ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa",
        "size": 100
      },
      "capture": "wire",
      "headers": {
        "content-length": "100",
        "content-type": "text/plain",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/unsupported/original/unsupported.txt",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "chunk_count": 1,
    "document_id": "unsupported",
    "source_name": "unsupported.txt",
    "source_type": "document",
    "vector_count": 1
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_requests": 0
}
```
</details>

## Filename normalization

A valid email is submitted with spaces, mixed case and a non-ASCII filename. Fixture: `baseline.eml` as `Café Guide V2.EML`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 400. Section/image storage attempts: Python 1/0, C# 0/0.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `400` |
| Ordered attempted operations | storage-original → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | No provider operation captured. |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml","x-upsert":"true"}` | `<MISSING>` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: DIFFERENT; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: DIFFERENT; vector_batch_sizes_at_capture_boundary: DIFFERENT; chunk boundaries/text differ; rows and vectors shown as unmatched ordered lists; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.status | `200` | `400` |
| endpoint.response.detail | `<MISSING>` | `"Invalid document ID derived from filename"` |
| endpoint.response.ingestion | `{"chunks_processed":1,"doc_id":"café-guide-v2","error":null,"images_processed":0,"message":"Successfully ingested Café Guide V2.EML","sections_processed":1,"source":"<documents>\\Café Guide V2.EML","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.supabase | `{"path":"docs/café-guide-v2/original/café-guide-v2.eml","stored":true}` | `<MISSING>` |
| ordered_operations[0] | `{"kind":"storage-original","provider":"supabase","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[1] | `{"kind":"storage-section","provider":"supabase","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[2] | `{"kind":"chunks-upsert","provider":"supabase","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[3] | `{"kind":"index-list","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[4] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-upsert | `[{"headers":{"accept":"*/*","content-length":"524","content-type":"application/json","prefer":"return=representation,resolution=merge-duplicates","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"},"method":"POST","origin":"https://provider.invalid","path":"/rest/v1/document_chunks","provider_response_status":200,"query":"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22","result":"canned-success"}]` | `<MISSING>` |
| supabase_http.storage-original | `[{"headers":{"accept":"*/*","content-length":"503","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"853","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"270","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original | `[{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml","x-upsert":"true"}]` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"café-guide-v2","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/café-guide-v2/sections/<section-1>.json","suggested_name":"café-guide-v2__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |
| chunk_batch_sizes[0] | `1` | `<MISSING>` |
| vector_batch_sizes_at_capture_boundary[0] | `1` | `<MISSING>` |
| chunk_rows.unmatched_boundaries | `[{"chunk_id":"<chunk-1>","content":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","doc_id":"café-guide-v2","end_seconds":null,"image_paths":[],"section_id":"<section-1>","section_title":"Synthetic upload baseline","source":"Café Guide V2.EML","source_type":"document","srt_url":null,"start_seconds":null,"txt_url":null,"video_url":null,"vtt_url":null}]` | `[]` |
| vectors.unmatched_chunks | `[{"id":"<chunk-1>","metadata":{"doc_id":"café-guide-v2","section_id":"<section-1>","source":"Café Guide V2.EML","source_type":"document"},"values":[162.0,644.0,407.0,17.0]}]` | `[]` |
| embedding_inputs_outputs.unmatched_chunks | `[{"input":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","values":[162.0,644.0,407.0,17.0]}]` | `[]` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162.0,
        644.0,
        407.0,
        17.0
      ]
    }
  ],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
              "size": 222
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"café-guide-v2.eml\"",
              "content-type": "message/rfc822"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "503",
        "content-type": "multipart/form-data; boundary=9b3a29a04bf543c72e16389ab42fa655",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"4deef583-29a1-460a-888c-0452c35d28b4.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
                  "type": "text"
                }
              ],
              "doc_slug": "café-guide-v2",
              "index": 1,
              "level": 1,
              "section_id": "4deef583-29a1-460a-888c-0452c35d28b4",
              "storage_path": "docs/café-guide-v2/sections/4deef583-29a1-460a-888c-0452c35d28b4.json",
              "suggested_name": "café-guide-v2__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "1f50503c3a747c630e981c1ec4eeea0bf82a4148a67791ea690bc71b30e95243",
            "size": 539
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "853",
        "content-type": "multipart/form-data; boundary=12aff868e432e49ebbc37b79f9c3d71d",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/4deef583-29a1-460a-888c-0452c35d28b4.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "9842e79c-6095-457b-b226-52e1ff8f10c0",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "café-guide-v2",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "4deef583-29a1-460a-888c-0452c35d28b4",
          "section_title": "Synthetic upload baseline",
          "source": "Café Guide V2.EML",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "524",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "9842e79c-6095-457b-b226-52e1ff8f10c0",
            "metadata": {
              "doc_id": "café-guide-v2",
              "section_id": "4deef583-29a1-460a-888c-0452c35d28b4",
              "source": "Café Guide V2.EML",
              "source_type": "document"
            },
            "values": [
              162.0,
              644.0,
              407.0,
              17.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "270",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "chunks_processed": 1,
      "doc_id": "café-guide-v2",
      "error": null,
      "images_processed": 0,
      "message": "Successfully ingested Café Guide V2.EML",
      "sections_processed": 1,
      "source": "<documents>\\Café Guide V2.EML",
      "success": true
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/café-guide-v2/original/café-guide-v2.eml",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [],
  "failure_intercepted": false,
  "failure_requested": "none",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [],
  "response": {
    "detail": "Invalid document ID derived from filename"
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 400,
  "unexpected_requests": 0
}
```
</details>

## Injected original storage failure

The original upload attempt fails; continuation and compensating attempts remain visible. Fixture: `baseline.eml` as `baseline.eml`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 503. Section/image storage attempts: Python 1/0, C# 0/0. Chunk rows and test vectors match after ID validation and aliasing.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `503` |
| Ordered attempted operations | storage-original [injected failure] → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | chunks-upsert → vectors-upsert → storage-original [injected failure] → vectors-delete → chunks-delete |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` |
| Failure hook intercepted | `true` | `true` |

**Checks:** endpoint.status: DIFFERENT; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: MATCH; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.status | `200` | `503` |
| endpoint.response.detail | `<MISSING>` | `"A dependency is unavailable"` |
| endpoint.response.ingestion | `{"chunks_processed":1,"doc_id":"baseline","error":null,"images_processed":0,"message":"Successfully ingested baseline.eml","sections_processed":1,"source":"<documents>\\baseline.eml","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.supabase | `{"error":"{'statusCode': 503, 'error': Injected, 'message': injected failure}","stored":false}` | `<MISSING>` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[0].result | `"injected-failure"` | `"canned-success"` |
| ordered_operations[1].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[2].kind | `"chunks-upsert"` | `"storage-original"` |
| ordered_operations[2].result | `"canned-success"` | `"injected-failure"` |
| ordered_operations[3].kind | `"index-list"` | `"vectors-delete"` |
| ordered_operations[4].kind | `"index-describe"` | `"chunks-delete"` |
| ordered_operations[4].provider | `"pinecone"` | `"supabase"` |
| ordered_operations[5] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-delete | `<MISSING>` | `[{"headers":{"prefer":"return=minimal"},"method":"DELETE","origin":"https://provider.invalid","path":"/rest/v1/document_chunks","provider_response_status":200,"query":"?chunk_id=in.%28%22<chunk-1>%22%29","result":"canned-success"}]` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"512"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"497"` | `"222"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"message/rfc822"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"835","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"258","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"baseline","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/baseline/sections/<section-1>.json","suggested_name":"baseline__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162.0,
        644.0,
        407.0,
        17.0
      ]
    }
  ],
  "failure_intercepted": true,
  "failure_requested": "storage-original",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
              "size": 222
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"baseline.eml\"",
              "content-type": "message/rfc822"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "497",
        "content-type": "multipart/form-data; boundary=c79e028bedcef8f7ff7b6e87371585ba",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 503,
      "query": "",
      "result": "injected-failure"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"3629443a-295b-47b3-af57-91147e7dc0b0.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
                  "type": "text"
                }
              ],
              "doc_slug": "baseline",
              "index": 1,
              "level": 1,
              "section_id": "3629443a-295b-47b3-af57-91147e7dc0b0",
              "storage_path": "docs/baseline/sections/3629443a-295b-47b3-af57-91147e7dc0b0.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "be91f0ce6a551b7aeaff59b8e178e60abb05db44680d296f86e429c3ed574149",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=3a2f37619cb10f050da87a3cbd8c0c2a",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/3629443a-295b-47b3-af57-91147e7dc0b0.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "597c308a-cc9b-4bbb-8166-e31abbc3a770",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "3629443a-295b-47b3-af57-91147e7dc0b0",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "512",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "597c308a-cc9b-4bbb-8166-e31abbc3a770",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "3629443a-295b-47b3-af57-91147e7dc0b0",
              "source": "baseline.eml",
              "source_type": "document"
            },
            "values": [
              162.0,
              644.0,
              407.0,
              17.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "258",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "ingestion": {
      "chunks_processed": 1,
      "doc_id": "baseline",
      "error": null,
      "images_processed": 0,
      "message": "Successfully ingested baseline.eml",
      "sections_processed": 1,
      "source": "<documents>\\baseline.eml",
      "success": true
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "error": "{'statusCode': 503, 'error': Injected, 'message': injected failure}",
      "stored": false
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162,
        644,
        407,
        17
      ]
    }
  ],
  "failure_intercepted": true,
  "failure_requested": "storage-original",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "5266e9df-7437-4d84-b5ce-4376e53ebaf2",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "bc74e8c0-4956-43ca-bf0a-b7c38545ef12",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "canned-success",
      "vectors": [
        {
          "id": "5266e9df-7437-4d84-b5ce-4376e53ebaf2",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "bc74e8c0-4956-43ca-bf0a-b7c38545ef12",
            "source": "baseline.eml",
            "source_type": "document"
          },
          "values": [
            162,
            644,
            407,
            17
          ]
        }
      ]
    },
    {
      "body": {
        "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
        "size": 222
      },
      "capture": "wire",
      "headers": {
        "content-length": "222",
        "content-type": "message/rfc822",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 503,
      "query": "",
      "result": "injected-failure"
    },
    {
      "capture": "application",
      "ids": [
        "5266e9df-7437-4d84-b5ce-4376e53ebaf2"
      ],
      "kind": "vectors-delete",
      "provider": "pinecone",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "prefer": "return=minimal"
      },
      "kind": "chunks-delete",
      "method": "DELETE",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?chunk_id=in.%28%225266e9df-7437-4d84-b5ce-4376e53ebaf2%22%29",
      "result": "canned-success"
    }
  ],
  "response": {
    "detail": "A dependency is unavailable"
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 503,
  "unexpected_requests": 0
}
```
</details>

## Injected chunk upsert failure

The chunk upsert attempt fails; later operations and compensation are recorded. Fixture: `baseline.eml` as `baseline.eml`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success false; C# HTTP 503. Section/image storage attempts: Python 1/0, C# 0/0.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `503` |
| Ordered attempted operations | storage-original → storage-section → chunks-upsert [injected failure] | chunks-upsert [injected failure] → chunks-delete |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` | `<MISSING>` |
| Failure hook intercepted | `true` | `true` |

**Checks:** endpoint.status: DIFFERENT; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: neither side captured SDK HTTP; storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: neither side sent anything; chunk_rows: MATCH; vectors: neither side sent anything; embedding_inputs_outputs: DIFFERENT; namespace: Python did not call its index; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.status | `200` | `503` |
| endpoint.response.detail | `<MISSING>` | `"A dependency is unavailable"` |
| endpoint.response.ingestion | `{"error":"500: {'message': 'JSON could not be generated', 'code': 503, 'hint': 'Refer to full message for details', 'details': 'b\\'{\"message\":\"injected failure\",\"error\":\"Injected\",\"statusCode\":503}\\''}","success":false}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.supabase | `{"path":"docs/baseline/original/baseline.eml","stored":true}` | `<MISSING>` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[0].result | `"canned-success"` | `"injected-failure"` |
| ordered_operations[1].kind | `"storage-section"` | `"chunks-delete"` |
| ordered_operations[2] | `{"kind":"chunks-upsert","provider":"supabase","result":"injected-failure"}` | `<MISSING>` |
| supabase_http.chunks-delete | `<MISSING>` | `[{"headers":{"prefer":"return=minimal"},"method":"DELETE","origin":"https://provider.invalid","path":"/rest/v1/document_chunks","provider_response_status":200,"query":"?chunk_id=in.%28%22<chunk-1>%22%29","result":"canned-success"}]` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"512"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original | `[{"headers":{"accept":"*/*","content-length":"497","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"835","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| storage_objects.storage-original | `[{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}]` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"baseline","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/baseline/sections/<section-1>.json","suggested_name":"baseline__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |
| embedding_inputs_outputs[0] | `{"input":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","values":[162.0,644.0,407.0,17.0]}` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162.0,
        644.0,
        407.0,
        17.0
      ]
    }
  ],
  "failure_intercepted": true,
  "failure_requested": "chunks-upsert",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
              "size": 222
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"baseline.eml\"",
              "content-type": "message/rfc822"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "497",
        "content-type": "multipart/form-data; boundary=25b95b0560f65f925daf27062d830d4a",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"9ea96440-6c2b-4d08-b68a-5f2b411d9cc4.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
                  "type": "text"
                }
              ],
              "doc_slug": "baseline",
              "index": 1,
              "level": 1,
              "section_id": "9ea96440-6c2b-4d08-b68a-5f2b411d9cc4",
              "storage_path": "docs/baseline/sections/9ea96440-6c2b-4d08-b68a-5f2b411d9cc4.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "7b7559a6854a218e931f0d0616ce82e07db5c92b715cd84157413bcf4c37ab6f",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=6bf11c9823c97f5219899c71c64bf214",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/9ea96440-6c2b-4d08-b68a-5f2b411d9cc4.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "46d32e9a-2f8d-421e-848a-298da4a172b4",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "9ea96440-6c2b-4d08-b68a-5f2b411d9cc4",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "512",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 503,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "injected-failure"
    }
  ],
  "response": {
    "ingestion": {
      "error": "500: {'message': 'JSON could not be generated', 'code': 503, 'hint': 'Refer to full message for details', 'details': 'b\\'{\"message\":\"injected failure\",\"error\":\"Injected\",\"statusCode\":503}\\''}",
      "success": false
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/baseline/original/baseline.eml",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [],
  "failure_intercepted": true,
  "failure_requested": "chunks-upsert",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "814529df-3683-4202-96b3-9062e6427475",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "ebecc28c-6aba-4a46-bf3d-1f19cf42daa6",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 503,
      "query": "?on_conflict=chunk_id",
      "result": "injected-failure"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "prefer": "return=minimal"
      },
      "kind": "chunks-delete",
      "method": "DELETE",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?chunk_id=in.%28%22814529df-3683-4202-96b3-9062e6427475%22%29",
      "result": "canned-success"
    }
  ],
  "response": {
    "detail": "A dependency is unavailable"
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 503,
  "unexpected_requests": 0
}
```
</details>

## Injected vector upsert failure

The vector boundary fails after chunk upsert; later operations and compensation are recorded. Fixture: `baseline.eml` as `baseline.eml`. Capture: Supabase transport; Pinecone Python transport / C# application boundary.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success false; C# HTTP 500. Section/image storage attempts: Python 1/0, C# 0/0. Chunk rows and test vectors match after ID validation and aliasing.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `500` |
| Ordered attempted operations | storage-original → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert [injected failure] | chunks-upsert → vectors-upsert [injected failure] → vectors-delete → chunks-delete |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}` | `<MISSING>` |
| Failure hook intercepted | `true` | `true` |

**Checks:** endpoint.status: DIFFERENT; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: MATCH; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.status | `200` | `500` |
| endpoint.response.detail | `<MISSING>` | `"Upload failed; contact an operator if reconciliation is needed"` |
| endpoint.response.ingestion | `{"error":"500: [400] injected failure","success":false}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.supabase | `{"path":"docs/baseline/original/baseline.eml","stored":true}` | `<MISSING>` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[1].result | `"canned-success"` | `"injected-failure"` |
| ordered_operations[2].kind | `"chunks-upsert"` | `"vectors-delete"` |
| ordered_operations[2].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[3].kind | `"index-list"` | `"chunks-delete"` |
| ordered_operations[3].provider | `"pinecone"` | `"supabase"` |
| ordered_operations[4] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"vectors-upsert","provider":"pinecone","result":"injected-failure"}` | `<MISSING>` |
| supabase_http.chunks-delete | `<MISSING>` | `[{"headers":{"prefer":"return=minimal"},"method":"DELETE","origin":"https://provider.invalid","path":"/rest/v1/document_chunks","provider_response_status":200,"query":"?chunk_id=in.%28%22<chunk-1>%22%29","result":"canned-success"}]` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"512"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original | `[{"headers":{"accept":"*/*","content-length":"497","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"835","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"258","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":400,"query":"","result":"injected-failure"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original | `[{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml","x-upsert":"true"}]` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"baseline","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/baseline/sections/<section-1>.json","suggested_name":"baseline__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/baseline/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |

<details><summary>Python: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "wire",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162.0,
        644.0,
        407.0,
        17.0
      ]
    }
  ],
  "failure_intercepted": true,
  "failure_requested": "vectors-upsert",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "serialized in Pinecone upsert JSON when present",
  "operations": [
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "binary": {
              "sha256": "24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092",
              "size": 222
            },
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"baseline.eml\"",
              "content-type": "message/rfc822"
            }
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "497",
        "content-type": "multipart/form-data; boundary=c0b5e7941f82a3844cd9ac6af84e6d0a",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-original",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/original/baseline.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "format": "multipart",
        "parts": [
          {
            "headers": {
              "content-disposition": "form-data; name=\"cacheControl\""
            },
            "text": "3600"
          },
          {
            "headers": {
              "content-disposition": "form-data; name=\"file\"; filename=\"6718ec24-97d2-4d5e-8664-99738996240d.json\"",
              "content-type": "text/plain; charset=\"UTF-8\""
            },
            "json": {
              "blocks": [
                {
                  "text": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
                  "type": "text"
                }
              ],
              "doc_slug": "baseline",
              "index": 1,
              "level": 1,
              "section_id": "6718ec24-97d2-4d5e-8664-99738996240d",
              "storage_path": "docs/baseline/sections/6718ec24-97d2-4d5e-8664-99738996240d.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "27bcf361f93138050e99b28de7d58a0c42f8fd58bb6b38bd83de8217c41cebeb",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=60bd1161c216d2d38d9edd0850927341",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/6718ec24-97d2-4d5e-8664-99738996240d.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "a5ff798c-ade8-44ec-bbb8-8a5631ec6551",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "6718ec24-97d2-4d5e-8664-99738996240d",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "512",
        "content-type": "application/json",
        "prefer": "return=representation,resolution=merge-duplicates",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-list",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "index-describe",
      "method": "GET",
      "origin": "https://api.pinecone.io",
      "path": "/indexes/parity-index",
      "provider": "pinecone",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": {
        "namespace": "parity-namespace",
        "vectors": [
          {
            "id": "a5ff798c-ade8-44ec-bbb8-8a5631ec6551",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "6718ec24-97d2-4d5e-8664-99738996240d",
              "source": "baseline.eml",
              "source_type": "document"
            },
            "values": [
              162.0,
              644.0,
              407.0,
              17.0
            ]
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "content-length": "258",
        "content-type": "application/json",
        "user-agent": "python-client-10.0.0",
        "x-pinecone-api-version": "2026-07"
      },
      "kind": "vectors-upsert",
      "method": "POST",
      "origin": "https://parity-index.invalid",
      "path": "/vectors/upsert",
      "provider": "pinecone",
      "provider_response_status": 400,
      "query": "",
      "result": "injected-failure"
    }
  ],
  "response": {
    "ingestion": {
      "error": "500: [400] injected failure",
      "success": false
    },
    "message": "File uploaded and ingestion triggered",
    "supabase": {
      "path": "docs/baseline/original/baseline.eml",
      "stored": true
    }
  },
  "self_test": {
    "dns_denied": true,
    "dotenv_reads_blocked": true,
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 200,
  "unexpected_network": 0,
  "unexpected_requests": 0
}
```
</details>

<details><summary>C#: complete sanitized capture</summary>

```json
{
  "capture_levels": {
    "pinecone": "application",
    "supabase": "wire"
  },
  "embeddings": [
    {
      "input": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
      "values": [
        162,
        644,
        407,
        17
      ]
    }
  ],
  "failure_intercepted": true,
  "failure_requested": "vectors-upsert",
  "namespace_configured": "parity-namespace",
  "namespace_observed": "unobservable at the substituted IVectorStore boundary",
  "operations": [
    {
      "body": [
        {
          "chunk_id": "54dbbbc9-bd6b-46e7-9095-21fcb12120d6",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "13eff5e4-27c6-4c99-b000-df4b2dc98b49",
          "section_title": "Synthetic upload baseline",
          "source": "baseline.eml",
          "source_type": "document",
          "srt_url": null,
          "start_seconds": null,
          "txt_url": null,
          "video_url": null,
          "vtt_url": null
        }
      ],
      "capture": "wire",
      "headers": {
        "content-type": "application/json; charset=utf-8",
        "prefer": "resolution=merge-duplicates,return=minimal"
      },
      "kind": "chunks-upsert",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?on_conflict=chunk_id",
      "result": "canned-success"
    },
    {
      "capture": "application",
      "kind": "vectors-upsert",
      "provider": "pinecone",
      "result": "injected-failure",
      "vectors": [
        {
          "id": "54dbbbc9-bd6b-46e7-9095-21fcb12120d6",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "13eff5e4-27c6-4c99-b000-df4b2dc98b49",
            "source": "baseline.eml",
            "source_type": "document"
          },
          "values": [
            162,
            644,
            407,
            17
          ]
        }
      ]
    },
    {
      "capture": "application",
      "ids": [
        "54dbbbc9-bd6b-46e7-9095-21fcb12120d6"
      ],
      "kind": "vectors-delete",
      "provider": "pinecone",
      "result": "canned-success"
    },
    {
      "body": null,
      "capture": "wire",
      "headers": {
        "prefer": "return=minimal"
      },
      "kind": "chunks-delete",
      "method": "DELETE",
      "origin": "https://provider.invalid",
      "path": "/rest/v1/document_chunks",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "?chunk_id=in.%28%2254dbbbc9-bd6b-46e7-9095-21fcb12120d6%22%29",
      "result": "canned-success"
    }
  ],
  "response": {
    "detail": "Upload failed; contact an operator if reconciliation is needed"
  },
  "self_test": {
    "in_process_server": true,
    "socket_denied": true,
    "unhandled_http_denied": true
  },
  "status": 500,
  "unexpected_requests": 0
}
```
</details>

## Optional real-model case

**SKIPPED:** matching Python and C# model versions/configuration and numeric tolerance were not established. The deterministic test vectors above are not substituted as evidence of real-model parity.

## Reproduction and verification

- Revision: `c938e952898cf17554d37581dcc68fbc3e41ed66` plus the working-tree harness/fixture changes; Python `3.14.7`, `fastapi` 0.142.2, `supabase` 2.32.0, `storage3` 2.32.0, `postgrest` 2.32.0, `httpx` 0.28.1, `pinecone` 10.0.0, `python-docx` 1.2.0; Python host `Windows`; .NET SDK 10.0.401 and Linux ASP.NET/.NET runtime 10.0.15 in `mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586`.
- All fixtures are synthetic. DOCX fixtures can be regenerated by `generate_docx_fixtures.py` with python-docx 1.2.0. Both hosts receive the same bytes and `file` field for each case.
- The report was regenerated from scratch. Two offline runs had identical normalized comparisons; original random IDs and multipart boundaries remain visible below.
- Prerequisites: Docker with the pinned image already present, .NET packages in the local NuGet cache, and the pinned Python packages in the local `uv` cache. Image/package acquisition is a separate setup step; no provider credentials are required. The commands below are the recorded Windows Git Bash/MSYS offline run. The runner fixes `PYTHONHASHSEED=0` in each Python child and performs two complete suites per invocation.

```bash
# One-time image acquisition was: docker pull mcr.microsoft.com/dotnet/aspnet:10.0
# Offline setup from already-populated caches:
uv venv --offline --python "$HOME/AppData/Roaming/uv/python/cpython-3.14-windows-x86_64-none/python.exe" "$(cygpath -aw /tmp/cfc-upload-parity-verified)"
uv pip install --offline --python "$(cygpath -aw /tmp/cfc-upload-parity-verified/Scripts/python.exe)" -r dotnet/tests/DocumentUploadParity/requirements-parity.txt
empty="$(mktemp -d)"
dotnet restore dotnet/tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --source "$(cygpath -w "$empty")"
rm -rf "$empty"
dotnet build dotnet/tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --no-restore
/tmp/cfc-upload-parity-verified/Scripts/python.exe -m unittest discover -s dotnet/tests/DocumentUploadParity -p 'test_*.py'
dotnet test dotnet/tests/CFC.ChatAI.DocumentProcessor.Tests/CFC.ChatAI.DocumentProcessor.Tests.csproj --no-restore --filter FullyQualifiedName~UploadParityFixtureTests
PYTHONHASHSEED=0 /tmp/cfc-upload-parity-verified/Scripts/python.exe dotnet/tests/DocumentUploadParity/run_parity.py
rm -rf /tmp/cfc-upload-parity-verified
```

Fixture hashes:

| Fixture | SHA-256 |
| --- | --- |
| `baseline.eml` | `24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092` |
| `embedded-image.docx` | `0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929` |
| `multi-section.docx` | `87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198` |
| `unsupported.txt` | `ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa` |

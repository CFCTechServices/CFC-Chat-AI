# Document upload request parity report

Synthetic, in-process upload investigation. `DIFFERENT` is an observation, not a failing test. A canned response records an **attempt**, not a remote side effect. No real provider writes occur.

| Case | Outcome | Operations Python / C# | Differences |
| --- | --- | ---: | ---: |
| Baseline EML | DIFFERENT | 6 / 3 | 33 |
| Multi-section DOCX | DIFFERENT | 7 / 3 | 34 |
| Embedded image DOCX | DIFFERENT | 7 / 3 | 37 |
| TXT upload | DIFFERENT | 1 / 3 | 23 |
| Filename normalization | DIFFERENT | 6 / 3 | 35 |
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
        "content-type": "multipart/form-data; boundary=57b3645676ed568d3be1907143ef1c59",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"27ea3f9d-1e69-47d5-b92e-137bc9b9f779.json\"",
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
              "section_id": "27ea3f9d-1e69-47d5-b92e-137bc9b9f779",
              "storage_path": "docs/baseline/sections/27ea3f9d-1e69-47d5-b92e-137bc9b9f779.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "30dfbb30ef3a53b98cd99cd65027f550d0f1c2156828285ebbca901a571f3755",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=ba88436222fbbabf65146a641de2d723",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/27ea3f9d-1e69-47d5-b92e-137bc9b9f779.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "2d4b55d1-fd8f-42df-9c46-51168d03a369",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "27ea3f9d-1e69-47d5-b92e-137bc9b9f779",
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
            "id": "2d4b55d1-fd8f-42df-9c46-51168d03a369",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "27ea3f9d-1e69-47d5-b92e-137bc9b9f779",
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
          "chunk_id": "3410fb30-ec9b-4e28-957a-7bb1a4c31e6a",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "842eb113-6188-487d-9dc3-7b4c15cf06b6",
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
          "id": "3410fb30-ec9b-4e28-957a-7bb1a4c31e6a",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "842eb113-6188-487d-9dc3-7b4c15cf06b6",
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
        "content-type": "multipart/form-data; boundary=37c822bb8cdd76bb598bf4636b14664b",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"e25c1c61-8b2f-4306-8bb2-f304ad4af004.json\"",
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
              "section_id": "e25c1c61-8b2f-4306-8bb2-f304ad4af004",
              "storage_path": "docs/multi-section/sections/e25c1c61-8b2f-4306-8bb2-f304ad4af004.json",
              "suggested_name": "multi-section__sec001_overview.json",
              "title": "Overview"
            },
            "sha256": "ef0527e4cd647f9c7536922b566fc3fd2d3a2b853da3be848da6e44a0202bbb8",
            "size": 384
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "698",
        "content-type": "multipart/form-data; boundary=6601a9114cd91c2ab4411df11b96b2c6",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/e25c1c61-8b2f-4306-8bb2-f304ad4af004.json",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"ca77419b-3137-43c5-8926-843e8cc0972b.json\"",
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
              "section_id": "ca77419b-3137-43c5-8926-843e8cc0972b",
              "storage_path": "docs/multi-section/sections/ca77419b-3137-43c5-8926-843e8cc0972b.json",
              "suggested_name": "multi-section__sec002_details.json",
              "title": "Details"
            },
            "sha256": "426515f9087833be84df40d16abae418d1135277d5fab712ec8a755c0c125cc9",
            "size": 383
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "697",
        "content-type": "multipart/form-data; boundary=9b456cc9779821caa7ae9cadb22d393a",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/ca77419b-3137-43c5-8926-843e8cc0972b.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "cf7b2000-7044-4716-b1e2-9dc41b8f9da1",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "e25c1c61-8b2f-4306-8bb2-f304ad4af004",
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
          "chunk_id": "a72ff4ef-d2ae-4d3f-ab7a-be28a7e817c2",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "ca77419b-3137-43c5-8926-843e8cc0972b",
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
            "id": "cf7b2000-7044-4716-b1e2-9dc41b8f9da1",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "e25c1c61-8b2f-4306-8bb2-f304ad4af004",
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
            "id": "a72ff4ef-d2ae-4d3f-ab7a-be28a7e817c2",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "ca77419b-3137-43c5-8926-843e8cc0972b",
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
          "chunk_id": "a8ebd05a-93ea-4798-9d25-1c271f337419",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "17e7f7b8-9a9e-45e4-ae23-6a134943c5d6",
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
          "chunk_id": "522890c1-f170-4407-b83b-94ec81571777",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "0b7ce039-f8e3-4106-8cdf-d7cee9a15940",
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
          "id": "a8ebd05a-93ea-4798-9d25-1c271f337419",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "17e7f7b8-9a9e-45e4-ae23-6a134943c5d6",
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
          "id": "522890c1-f170-4407-b83b-94ec81571777",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "0b7ce039-f8e3-4106-8cdf-d7cee9a15940",
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
        "content-type": "multipart/form-data; boundary=5c4117f3396fe4062faa471663e2232f",
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
        "content-type": "multipart/form-data; boundary=206ca50d074b7daf35dd4440b7446cb5",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"c2483e50-e2c7-4fd7-912a-6144e7ad20ba.json\"",
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
              "section_id": "c2483e50-e2c7-4fd7-912a-6144e7ad20ba",
              "storage_path": "docs/embedded-image/sections/c2483e50-e2c7-4fd7-912a-6144e7ad20ba.json",
              "suggested_name": "embedded-image__sec001_illustration.json",
              "title": "Illustration"
            },
            "sha256": "6ffdb56824b7f2149d229a3b74012957448135f5a65274d498b975fd55535588",
            "size": 671
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "985",
        "content-type": "multipart/form-data; boundary=11d1ec2fe6276f3a999ed7d2e6e429b3",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/sections/c2483e50-e2c7-4fd7-912a-6144e7ad20ba.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "6bf0c1ca-3471-4478-8b34-ebc8bc86639e",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [
            "docs/embedded-image/images/embedded-image__img001.png"
          ],
          "section_id": "c2483e50-e2c7-4fd7-912a-6144e7ad20ba",
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
            "id": "6bf0c1ca-3471-4478-8b34-ebc8bc86639e",
            "metadata": {
              "doc_id": "embedded-image",
              "section_id": "c2483e50-e2c7-4fd7-912a-6144e7ad20ba",
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
          "chunk_id": "5777ac05-9958-4d7b-b709-7181ab4aa069",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "6f268634-a44a-40e9-8040-d9e73abedaa7",
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
          "id": "5777ac05-9958-4d7b-b709-7181ab4aa069",
          "metadata": {
            "doc_id": "embedded-image",
            "section_id": "6f268634-a44a-40e9-8040-d9e73abedaa7",
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
        "content-type": "multipart/form-data; boundary=9c2e344897f4676e3d0b2112e556bab6",
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
          "chunk_id": "0cec7f1a-3664-4db0-a303-6c114df28686",
          "content": "This synthetic text fixture is supported by the C# processor but not the Python document processor.",
          "doc_id": "unsupported",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "1f9977e0-c350-46ef-a42a-0fefb0f5ffbf",
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
          "id": "0cec7f1a-3664-4db0-a303-6c114df28686",
          "metadata": {
            "doc_id": "unsupported",
            "section_id": "1f9977e0-c350-46ef-a42a-0fefb0f5ffbf",
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

A valid email is submitted with spaces, mixed case and a non-ASCII filename. Fixture: `baseline.eml` as `Café Guide V2.EML`. Capture: Supabase transport; Pinecone Python transport / C# application boundary. Both endpoints must accept the file and report `café-guide-v2`. C# intentionally encodes the new original's storage key to meet Supabase's ASCII key restriction.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 200. Section/image storage attempts: Python 1/0, C# 0/0. Chunk rows and test vectors match after ID validation and aliasing.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | chunks-upsert → vectors-upsert → storage-original |
| Original object and options | `{"kind":"storage-original","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml","x-upsert":"true"}` | `{"kind":"storage-original","multipart_fields":[],"object":{"sha256":"24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092","size":222},"object_content_type":"message/rfc822","path":"/storage/v1/object/parity-bucket/docs/%21u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml","x-upsert":"true"}` |
| Failure hook intercepted | `false` | `false` |

**Checks:** endpoint.status: MATCH; endpoint.response: DIFFERENT; ordered_operations: DIFFERENT; supabase_http: DIFFERENT; pinecone_http: NOT COMPARABLE (one side bypasses the SDK); storage_objects: DIFFERENT; chunk_batch_sizes: MATCH; vector_batch_sizes_at_capture_boundary: MATCH; chunk_rows: MATCH; vectors: MATCH; embedding_inputs_outputs: MATCH; namespace: Python captured "parity-namespace"; C# is unobservable at its fake vector store.

| Difference (normalized only as described above) | Python | C# |
| --- | --- | --- |
| endpoint.response.chunk_count | `<MISSING>` | `1` |
| endpoint.response.document_id | `<MISSING>` | `"café-guide-v2"` |
| endpoint.response.ingestion | `{"chunks_processed":1,"doc_id":"café-guide-v2","error":null,"images_processed":0,"message":"Successfully ingested Café Guide V2.EML","sections_processed":1,"source":"<documents>\\Café Guide V2.EML","success":true}` | `<MISSING>` |
| endpoint.response.message | `"File uploaded and ingestion triggered"` | `<MISSING>` |
| endpoint.response.source_name | `<MISSING>` | `"Café Guide V2.EML"` |
| endpoint.response.source_type | `<MISSING>` | `"document"` |
| endpoint.response.supabase | `{"path":"docs/café-guide-v2/original/café-guide-v2.eml","stored":true}` | `<MISSING>` |
| endpoint.response.vector_count | `<MISSING>` | `1` |
| ordered_operations[0].kind | `"storage-original"` | `"chunks-upsert"` |
| ordered_operations[1].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[1].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[2].kind | `"chunks-upsert"` | `"storage-original"` |
| ordered_operations[3] | `{"kind":"index-list","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[4] | `{"kind":"index-describe","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| ordered_operations[5] | `{"kind":"vectors-upsert","provider":"pinecone","result":"canned-success"}` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-length | `"524"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.content-type | `"application/json"` | `"application/json; charset=utf-8"` |
| supabase_http.chunks-upsert[0].headers.prefer | `"return=representation,resolution=merge-duplicates"` | `"resolution=merge-duplicates,return=minimal"` |
| supabase_http.chunks-upsert[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.chunks-upsert[0].query | `"?columns=%22chunk_id%22%2C%22section_id%22%2C%22source_type%22%2C%22source%22%2C%22video_url%22%2C%22vtt_url%22%2C%22start_seconds%22%2C%22image_paths%22%2C%22end_seconds%22%2C%22srt_url%22%2C%22txt_url%22%2C%22doc_id%22%2C%22content%22%2C%22section_title%22"` | `"?on_conflict=chunk_id"` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"503"` | `"222"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"message/rfc822"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-original[0].path | `"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml"` | `"/storage/v1/object/parity-bucket/docs/%21u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml"` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"853","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"270","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-original[0].path | `"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/original/caf%C3%A9-guide-v2.eml"` | `"/storage/v1/object/parity-bucket/docs/%21u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml"` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.","type":"text"}],"doc_slug":"café-guide-v2","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/café-guide-v2/sections/<section-1>.json","suggested_name":"café-guide-v2__sec001_email.json","title":"Synthetic upload baseline"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |

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
        "content-type": "multipart/form-data; boundary=26b23e6c1b6fadd2c5f6812772b141eb",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"77b65b68-220e-4033-a798-46e482dfe654.json\"",
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
              "section_id": "77b65b68-220e-4033-a798-46e482dfe654",
              "storage_path": "docs/café-guide-v2/sections/77b65b68-220e-4033-a798-46e482dfe654.json",
              "suggested_name": "café-guide-v2__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "e3a6cbb85b8cffe4287f789c9a6e118f903f837fa22c71ac9f62613214cb5261",
            "size": 539
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "853",
        "content-type": "multipart/form-data; boundary=efb7c5c4b665bc5711bc9f2c2c4a6c06",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/77b65b68-220e-4033-a798-46e482dfe654.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "184c730b-a01f-48ef-b6fd-43cb143f9847",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "café-guide-v2",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "77b65b68-220e-4033-a798-46e482dfe654",
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
            "id": "184c730b-a01f-48ef-b6fd-43cb143f9847",
            "metadata": {
              "doc_id": "café-guide-v2",
              "section_id": "77b65b68-220e-4033-a798-46e482dfe654",
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
          "chunk_id": "3f9879ed-f8de-48f9-bb96-c9926dbd6014",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "café-guide-v2",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "dc3fe585-4da1-4d97-8c1c-511d62646815",
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
          "id": "3f9879ed-f8de-48f9-bb96-c9926dbd6014",
          "metadata": {
            "doc_id": "café-guide-v2",
            "section_id": "dc3fe585-4da1-4d97-8c1c-511d62646815",
            "source": "Café Guide V2.EML",
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
      "path": "/storage/v1/object/parity-bucket/docs/%21u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    }
  ],
  "response": {
    "chunk_count": 1,
    "document_id": "café-guide-v2",
    "source_name": "Café Guide V2.EML",
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
        "content-type": "multipart/form-data; boundary=a4c982a39b5d87f12f1887c8c1978efe",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"a2140883-c584-4561-b802-121a83d0aace.json\"",
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
              "section_id": "a2140883-c584-4561-b802-121a83d0aace",
              "storage_path": "docs/baseline/sections/a2140883-c584-4561-b802-121a83d0aace.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "304db859937cd09f33ce69c543d0a69b9250da6178d174636d722b8707808176",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=fc0d2cc175cf47e2eba56b457e7aa82f",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/a2140883-c584-4561-b802-121a83d0aace.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "2b070555-6152-4d91-8607-712a018fdf15",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "a2140883-c584-4561-b802-121a83d0aace",
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
            "id": "2b070555-6152-4d91-8607-712a018fdf15",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "a2140883-c584-4561-b802-121a83d0aace",
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
          "chunk_id": "3b4a399c-2322-40c4-b4b7-bf8c9f3a09fa",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "2cdb2a34-af87-439d-9798-459ea08b81df",
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
          "id": "3b4a399c-2322-40c4-b4b7-bf8c9f3a09fa",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "2cdb2a34-af87-439d-9798-459ea08b81df",
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
        "3b4a399c-2322-40c4-b4b7-bf8c9f3a09fa"
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
      "query": "?chunk_id=in.%28%223b4a399c-2322-40c4-b4b7-bf8c9f3a09fa%22%29",
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
        "content-type": "multipart/form-data; boundary=941323f42def34b95c3eb484d43cdac6",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"2750afcf-6418-4161-94fc-a1386670d2a1.json\"",
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
              "section_id": "2750afcf-6418-4161-94fc-a1386670d2a1",
              "storage_path": "docs/baseline/sections/2750afcf-6418-4161-94fc-a1386670d2a1.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "8f4b1beebbddc67ac872e8171b9c89dddf9d3efc66281150a173a5a0df61123d",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=e1467e95ac17ccf17c785cc3b2f442c3",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/2750afcf-6418-4161-94fc-a1386670d2a1.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "5dad8e10-2c3a-44ee-8720-7f9fbf2393cc",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "2750afcf-6418-4161-94fc-a1386670d2a1",
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
          "chunk_id": "735d1a42-c91d-4a80-842b-1aa27fb86a98",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "48fedc4d-c189-4fba-88ce-5e06426ac4cc",
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
      "query": "?chunk_id=in.%28%22735d1a42-c91d-4a80-842b-1aa27fb86a98%22%29",
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
        "content-type": "multipart/form-data; boundary=c24be6e97cd1d3b26acea93aa777a9c7",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"67d7fb73-dc96-4eb7-ba85-cb78476f5e78.json\"",
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
              "section_id": "67d7fb73-dc96-4eb7-ba85-cb78476f5e78",
              "storage_path": "docs/baseline/sections/67d7fb73-dc96-4eb7-ba85-cb78476f5e78.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "118e0b1f5cd887b59794b825581bd8b928ef711fb40dea3e77b1b12b5a7ca443",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=e50c54025f2d30902552c21b20c5a73e",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/67d7fb73-dc96-4eb7-ba85-cb78476f5e78.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "00f7b0d3-7b4f-402f-b326-a5b5380fdab6",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "67d7fb73-dc96-4eb7-ba85-cb78476f5e78",
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
            "id": "00f7b0d3-7b4f-402f-b326-a5b5380fdab6",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "67d7fb73-dc96-4eb7-ba85-cb78476f5e78",
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
          "chunk_id": "7933c805-55c7-4ebb-9245-777f3cd6b836",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "4d562eed-e1a3-453d-b1fc-42cf726f56be",
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
          "id": "7933c805-55c7-4ebb-9245-777f3cd6b836",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "4d562eed-e1a3-453d-b1fc-42cf726f56be",
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
        "7933c805-55c7-4ebb-9245-777f3cd6b836"
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
      "query": "?chunk_id=in.%28%227933c805-55c7-4ebb-9245-777f3cd6b836%22%29",
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

- Revision: `57c966855a23623847007c5f611b4d642d48df71` plus the working-tree harness/fixture changes; Python `3.14.7`, `fastapi` 0.142.2, `supabase` 2.32.0, `storage3` 2.32.0, `postgrest` 2.32.0, `httpx` 0.28.1, `pinecone` 10.0.0, `python-docx` 1.2.0; Python host `Windows`; .NET SDK 10.0.401 and Linux ASP.NET/.NET runtime 10.0.15 in `mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586`.
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

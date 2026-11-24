# Document upload request parity report

Synthetic, in-process upload investigation. `DIFFERENT` is an observation, not a failing test. A canned response records an **attempt**, not a remote side effect. No real provider writes occur.

| Case | Outcome | Operations Python / C# | Differences |
| --- | --- | ---: | ---: |
| Baseline EML | DIFFERENT | 6 / 3 | 33 |
| Multi-section DOCX | DIFFERENT | 7 / 3 | 34 |
| Embedded image DOCX | DIFFERENT | 7 / 4 | 47 |
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
- Before per-side section/chunk aliases are assigned, section-to-row and row-to-vector references are validated where observable. C# emits no section artifacts, so its section IDs are first observed in rows and empty sections are unobservable. Different chunk boundaries are displayed as unmatched ordered lists. For image paths only, the random .NET ingest ID is shown as `<image-ingest>` in comparisons. Other paths, filenames, field presence, types, request order and batches are not normalized. Multipart boundary strings are normalized only in comparisons; original IDs and boundaries remain in details. Object key order is ignored; array order is retained. `<MISSING>` and `null` are distinct; `<NOT OBSERVABLE>` denotes the C# SDK capture limitation, not an absent request. Difference counts include these coverage gaps.
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
        "content-type": "multipart/form-data; boundary=b00a1f13d6f890ae2028a82d86c3486f",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d.json\"",
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
              "section_id": "e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d",
              "storage_path": "docs/baseline/sections/e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "d9a7fe679f59249cb84f433ddfaef83b768829ae5c98f7d6fe227976c59b95b0",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=ab0e9fc219e1dec776f1d5eb05115c73",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "e035e5c0-cbb5-40ed-a8e7-164d7dc7d938",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d",
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
            "id": "e035e5c0-cbb5-40ed-a8e7-164d7dc7d938",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "e2e3c18f-2a8c-4084-95d8-d32d7dec2a1d",
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
          "chunk_id": "fa632244-758a-403c-8f11-87a9c792dc00",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "f52a42d5-02ee-4caa-8d81-fd37d1659c84",
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
          "id": "fa632244-758a-403c-8f11-87a9c792dc00",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "f52a42d5-02ee-4caa-8d81-fd37d1659c84",
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
        "content-type": "multipart/form-data; boundary=d3c070b56ebe0fec10845d18c8824a69",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"33276b1e-9980-42b7-a533-fd6ef8cb629b.json\"",
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
              "section_id": "33276b1e-9980-42b7-a533-fd6ef8cb629b",
              "storage_path": "docs/multi-section/sections/33276b1e-9980-42b7-a533-fd6ef8cb629b.json",
              "suggested_name": "multi-section__sec001_overview.json",
              "title": "Overview"
            },
            "sha256": "3068e1c61d9a163e19550ffa3d448134f17193cebd6b42deab22d93653368bfb",
            "size": 384
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "698",
        "content-type": "multipart/form-data; boundary=96c22becb7dee8e631358301c9f28081",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/33276b1e-9980-42b7-a533-fd6ef8cb629b.json",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"89c5f608-a5b7-47d8-a3f5-67216ecf68a8.json\"",
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
              "section_id": "89c5f608-a5b7-47d8-a3f5-67216ecf68a8",
              "storage_path": "docs/multi-section/sections/89c5f608-a5b7-47d8-a3f5-67216ecf68a8.json",
              "suggested_name": "multi-section__sec002_details.json",
              "title": "Details"
            },
            "sha256": "ad14e5e56f1012c2af969ccbabcffb6d4f6cc06e622df75d19998bc12e1bd273",
            "size": 383
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "697",
        "content-type": "multipart/form-data; boundary=c138c26490a94a60d1d3772b847c1b54",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/multi-section/sections/89c5f608-a5b7-47d8-a3f5-67216ecf68a8.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "285d197b-313d-4d99-8971-8dbc4ef8495c",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "33276b1e-9980-42b7-a533-fd6ef8cb629b",
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
          "chunk_id": "331dc7c4-6f7f-45b4-883b-0953d08a5ca8",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "89c5f608-a5b7-47d8-a3f5-67216ecf68a8",
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
            "id": "285d197b-313d-4d99-8971-8dbc4ef8495c",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "33276b1e-9980-42b7-a533-fd6ef8cb629b",
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
            "id": "331dc7c4-6f7f-45b4-883b-0953d08a5ca8",
            "metadata": {
              "doc_id": "multi-section",
              "section_id": "89c5f608-a5b7-47d8-a3f5-67216ecf68a8",
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
          "chunk_id": "3c58289f-b7c9-4fec-8d81-749d59560ccb",
          "content": "Synthetic first section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "9d6dd523-aa94-4309-8bf3-5634b209bfb5",
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
          "chunk_id": "e38beabc-44a3-46ed-9222-982787d91996",
          "content": "Synthetic second section.",
          "doc_id": "multi-section",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "ab6fd169-008c-4d9f-a728-d49830602274",
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
          "id": "3c58289f-b7c9-4fec-8d81-749d59560ccb",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "9d6dd523-aa94-4309-8bf3-5634b209bfb5",
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
          "id": "e38beabc-44a3-46ed-9222-982787d91996",
          "metadata": {
            "doc_id": "multi-section",
            "section_id": "ab6fd169-008c-4d9f-a728-d49830602274",
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

A one-pixel image exercises image artifacts and chunk image references. Fixture: `embedded-image.docx` as `embedded-image.docx`. Capture: Supabase transport; Pinecone Python transport / C# application boundary. Both endpoints must persist an image reference that points to the captured PNG bytes. C# uploads with `image/png` and no overwrite, under an ingest-specific key used for safe rollback.

**Outcome:** DIFFERENT. Python HTTP 200 / ingestion success true; C# HTTP 200. Section/image storage attempts: Python 1/1, C# 0/1.

| Observation | Python | C# |
| --- | --- | --- |
| Endpoint status | `200` | `200` |
| Ordered attempted operations | storage-original → storage-image → storage-section → chunks-upsert → index-list → index-describe → vectors-upsert | storage-image → chunks-upsert → vectors-upsert → storage-original |
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
| ordered_operations[0].kind | `"storage-original"` | `"storage-image"` |
| ordered_operations[1].kind | `"storage-image"` | `"chunks-upsert"` |
| ordered_operations[2].kind | `"storage-section"` | `"vectors-upsert"` |
| ordered_operations[2].provider | `"supabase"` | `"pinecone"` |
| ordered_operations[3].kind | `"chunks-upsert"` | `"storage-original"` |
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
| supabase_http.storage-image[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-image[0].headers.content-length | `"367"` | `"68"` |
| supabase_http.storage-image[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"image/png"` |
| supabase_http.storage-image[0].headers.contenttype | `"image/png"` | `<MISSING>` |
| supabase_http.storage-image[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-image[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-image[0].headers.x-upsert | `"true"` | `"false"` |
| supabase_http.storage-image[0].path | `"/storage/v1/object/parity-bucket/docs/embedded-image/images/embedded-image__img001.png"` | `"/storage/v1/object/parity-bucket/docs/embedded-image/images/<image-ingest>__img001.png"` |
| supabase_http.storage-original[0].headers.accept | `"*/*"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.content-length | `"36139"` | `"35800"` |
| supabase_http.storage-original[0].headers.content-type | `"multipart/form-data; boundary=<boundary>"` | `"application/vnd.openxmlformats-officedocument.wordprocessingml.document"` |
| supabase_http.storage-original[0].headers.user-agent | `"python-httpx/0.28.1"` | `<MISSING>` |
| supabase_http.storage-original[0].headers.x-client-info | `"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7"` | `<MISSING>` |
| supabase_http.storage-section | `[{"headers":{"accept":"*/*","content-length":"985","content-type":"multipart/form-data; boundary=<boundary>","user-agent":"python-httpx/0.28.1","x-client-info":"supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7","x-upsert":"true"},"method":"POST","origin":"https://provider.invalid","path":"/storage/v1/object/parity-bucket/docs/embedded-image/sections/<section-1>.json","provider_response_status":200,"query":"","result":"canned-success"}]` | `<MISSING>` |
| pinecone_http.index-describe | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes/parity-index","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.index-list | `[{"headers":{"accept":"*/*","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"GET","origin":"https://api.pinecone.io","path":"/indexes","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| pinecone_http.vectors-upsert | `[{"headers":{"content-length":"268","content-type":"application/json","user-agent":"python-client-10.0.0","x-pinecone-api-version":"2026-07"},"method":"POST","origin":"https://parity-index.invalid","path":"/vectors/upsert","provider_response_status":200,"query":"","result":"canned-success"}]` | `<NOT OBSERVABLE>` |
| storage_objects.storage-image[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-image[0].object_content_type | `"text/plain; charset=\"UTF-8\""` | `"image/png"` |
| storage_objects.storage-image[0].path | `"/storage/v1/object/parity-bucket/docs/embedded-image/images/embedded-image__img001.png"` | `"/storage/v1/object/parity-bucket/docs/embedded-image/images/<image-ingest>__img001.png"` |
| storage_objects.storage-image[0].x-upsert | `"true"` | `"false"` |
| storage_objects.storage-original[0].multipart_fields[0] | `{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}` | `<MISSING>` |
| storage_objects.storage-section | `[{"kind":"storage-section","multipart_fields":[{"headers":{"content-disposition":"form-data; name=\"cacheControl\""},"text":"3600"}],"object":{"blocks":[{"text":"Synthetic text before the image.","type":"text"},{"path":"docs/embedded-image/images/embedded-image__img001.png","storage_path":"docs/embedded-image/images/embedded-image__img001.png","type":"image"},{"text":"Synthetic text after the image.","type":"text"}],"doc_slug":"embedded-image","index":1,"level":1,"section_id":"<section-1>","storage_path":"docs/embedded-image/sections/<section-1>.json","suggested_name":"embedded-image__sec001_illustration.json","title":"Illustration"},"object_content_type":"text/plain; charset=\"UTF-8\"","path":"/storage/v1/object/parity-bucket/docs/embedded-image/sections/<section-1>.json","x-upsert":"true"}]` | `<MISSING>` |
| chunk_rows[0].image_paths[0] | `"docs/embedded-image/images/embedded-image__img001.png"` | `"docs/embedded-image/images/<image-ingest>__img001.png"` |

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
        "content-type": "multipart/form-data; boundary=aa0284d3246ba766067b4bdd8fbded73",
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
        "content-type": "multipart/form-data; boundary=8bb111dd8df99283a60a8274a1fb3a9e",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"60846c6c-489c-45bb-ab93-083b36bf1c47.json\"",
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
              "section_id": "60846c6c-489c-45bb-ab93-083b36bf1c47",
              "storage_path": "docs/embedded-image/sections/60846c6c-489c-45bb-ab93-083b36bf1c47.json",
              "suggested_name": "embedded-image__sec001_illustration.json",
              "title": "Illustration"
            },
            "sha256": "3b35c384d365b719fcd4a2483fa336e82e78ce447d59c47e3058b39d330701eb",
            "size": 671
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "985",
        "content-type": "multipart/form-data; boundary=e042b8c6d713ca3c362aed030968a43b",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/sections/60846c6c-489c-45bb-ab93-083b36bf1c47.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "a70409bf-5a6e-4bf1-b022-a4b30c05ec2e",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [
            "docs/embedded-image/images/embedded-image__img001.png"
          ],
          "section_id": "60846c6c-489c-45bb-ab93-083b36bf1c47",
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
            "id": "a70409bf-5a6e-4bf1-b022-a4b30c05ec2e",
            "metadata": {
              "doc_id": "embedded-image",
              "section_id": "60846c6c-489c-45bb-ab93-083b36bf1c47",
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
      "body": {
        "sha256": "f688dd6e21f010dd1052e979d9bd19c3a054ca0266e15e5e5673f97e3f7f9271",
        "size": 68
      },
      "capture": "wire",
      "headers": {
        "content-length": "68",
        "content-type": "image/png",
        "x-upsert": "false"
      },
      "kind": "storage-image",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/embedded-image/images/63542b4b96d043b89ea0e64667b142b0__img001.png",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "16ede22c-8c5d-4f55-b64a-cc5e08a5759b",
          "content": "Synthetic text before the image. Synthetic text after the image.",
          "doc_id": "embedded-image",
          "end_seconds": null,
          "image_paths": [
            "docs/embedded-image/images/63542b4b96d043b89ea0e64667b142b0__img001.png"
          ],
          "section_id": "f46cf731-4d7a-4891-9af9-bd3ed362b2aa",
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
          "id": "16ede22c-8c5d-4f55-b64a-cc5e08a5759b",
          "metadata": {
            "doc_id": "embedded-image",
            "section_id": "f46cf731-4d7a-4891-9af9-bd3ed362b2aa",
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
        "content-type": "multipart/form-data; boundary=7b1558526667e2a7d51230b04856d267",
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
          "chunk_id": "cff79ea5-43da-4b49-9630-18c87d0a1e5a",
          "content": "This synthetic text fixture is supported by the C# processor but not the Python document processor.",
          "doc_id": "unsupported",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "48a8486b-ed99-4497-b31d-c525fca33ebc",
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
          "id": "cff79ea5-43da-4b49-9630-18c87d0a1e5a",
          "metadata": {
            "doc_id": "unsupported",
            "section_id": "48a8486b-ed99-4497-b31d-c525fca33ebc",
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
        "content-type": "multipart/form-data; boundary=5ab6612a1030eaa61d5ff08e7f4e7133",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f.json\"",
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
              "section_id": "adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f",
              "storage_path": "docs/café-guide-v2/sections/adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f.json",
              "suggested_name": "café-guide-v2__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "7f22b22d7af74f02036da6f4cda0cba84568f9e625a7f10b44ba9a505a3f9b23",
            "size": 539
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "853",
        "content-type": "multipart/form-data; boundary=4d1fd0ee53c66bcfd5c913cd597dbc1a",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/caf%C3%A9-guide-v2/sections/adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "52df4ec9-ad09-47ed-aa19-e6e6c9d68f2b",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "café-guide-v2",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f",
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
            "id": "52df4ec9-ad09-47ed-aa19-e6e6c9d68f2b",
            "metadata": {
              "doc_id": "café-guide-v2",
              "section_id": "adb2a4c7-dc5f-4d30-81c8-8367a0a49f4f",
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
          "chunk_id": "d6d24772-4235-4491-8a19-ced7f0ff637e",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "café-guide-v2",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "14e1b7b8-aaff-405f-ad11-fd5ee80056db",
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
          "id": "d6d24772-4235-4491-8a19-ced7f0ff637e",
          "metadata": {
            "doc_id": "café-guide-v2",
            "section_id": "14e1b7b8-aaff-405f-ad11-fd5ee80056db",
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
        "content-type": "multipart/form-data; boundary=cb8a8686e019f0880fa47ae712dd13e6",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"89511574-8644-4419-8bdd-62b27e980c17.json\"",
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
              "section_id": "89511574-8644-4419-8bdd-62b27e980c17",
              "storage_path": "docs/baseline/sections/89511574-8644-4419-8bdd-62b27e980c17.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "e7affbeddb13549b0216497a81fef1e0af50338d817fa388a36aa5fbb7d0f81f",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=91146cf57e4a3d2389268034f1527cd4",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/89511574-8644-4419-8bdd-62b27e980c17.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "d0b97429-e41b-460d-bf93-fbecdc6d63e6",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "89511574-8644-4419-8bdd-62b27e980c17",
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
            "id": "d0b97429-e41b-460d-bf93-fbecdc6d63e6",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "89511574-8644-4419-8bdd-62b27e980c17",
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
          "chunk_id": "5a9307f5-5607-4e9c-8993-d46f93ae63f2",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "65528598-a546-46cc-b8d7-8da4845c2e09",
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
          "id": "5a9307f5-5607-4e9c-8993-d46f93ae63f2",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "65528598-a546-46cc-b8d7-8da4845c2e09",
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
        "5a9307f5-5607-4e9c-8993-d46f93ae63f2"
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
      "query": "?chunk_id=in.%28%225a9307f5-5607-4e9c-8993-d46f93ae63f2%22%29",
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
        "content-type": "multipart/form-data; boundary=2eb0acb5b3924eb1382887da9b888124",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"430f8cf8-c10a-4bf9-ac27-afdd2c9a78f4.json\"",
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
              "section_id": "430f8cf8-c10a-4bf9-ac27-afdd2c9a78f4",
              "storage_path": "docs/baseline/sections/430f8cf8-c10a-4bf9-ac27-afdd2c9a78f4.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "c63ec41c191533d68253527cf722f6818d0b45d8886a3202c50347e8c4fdda38",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=da724cc604ddf76531fa56a37dec49f4",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/430f8cf8-c10a-4bf9-ac27-afdd2c9a78f4.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "333135b3-3a76-48eb-94e8-b070c1c364c6",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "430f8cf8-c10a-4bf9-ac27-afdd2c9a78f4",
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
          "chunk_id": "1cebe087-4223-445f-b0eb-69e7a6cba21b",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "3b3098b3-06f5-4673-b6e1-60a95285f21a",
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
      "query": "?chunk_id=in.%28%221cebe087-4223-445f-b0eb-69e7a6cba21b%22%29",
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
        "content-type": "multipart/form-data; boundary=626c1a31677d52c476504622eac10684",
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
              "content-disposition": "form-data; name=\"file\"; filename=\"bb7b1146-53e3-4fd9-97e4-612ed46db602.json\"",
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
              "section_id": "bb7b1146-53e3-4fd9-97e4-612ed46db602",
              "storage_path": "docs/baseline/sections/bb7b1146-53e3-4fd9-97e4-612ed46db602.json",
              "suggested_name": "baseline__sec001_email.json",
              "title": "Synthetic upload baseline"
            },
            "sha256": "12290bbc2e08982ac387ea8e380465bd832ea14beccd49373dbad80e1c329242",
            "size": 521
          }
        ]
      },
      "capture": "wire",
      "headers": {
        "accept": "*/*",
        "content-length": "835",
        "content-type": "multipart/form-data; boundary=aa97bad5583ec8b0a93bbd5be95b51a8",
        "user-agent": "python-httpx/0.28.1",
        "x-client-info": "supabase-py/2.32.0; platform=Windows; platform-version=11; runtime=python; runtime-version=3.14.7",
        "x-upsert": "true"
      },
      "kind": "storage-section",
      "method": "POST",
      "origin": "https://provider.invalid",
      "path": "/storage/v1/object/parity-bucket/docs/baseline/sections/bb7b1146-53e3-4fd9-97e4-612ed46db602.json",
      "provider": "supabase",
      "provider_response_status": 200,
      "query": "",
      "result": "canned-success"
    },
    {
      "body": [
        {
          "chunk_id": "85b5bf1d-33da-4e44-84d5-67170e40475b",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "bb7b1146-53e3-4fd9-97e4-612ed46db602",
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
            "id": "85b5bf1d-33da-4e44-84d5-67170e40475b",
            "metadata": {
              "doc_id": "baseline",
              "section_id": "bb7b1146-53e3-4fd9-97e4-612ed46db602",
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
          "chunk_id": "ebb88eb3-304a-4283-bfeb-fa353e486a86",
          "content": "Subject: Synthetic upload baseline From: sender@example.invalid To: reader@example.invalid This is synthetic content for the document upload parity investigation.",
          "doc_id": "baseline",
          "end_seconds": null,
          "image_paths": [],
          "section_id": "24258661-11f2-4b0d-bd15-f4ca163ad6df",
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
          "id": "ebb88eb3-304a-4283-bfeb-fa353e486a86",
          "metadata": {
            "doc_id": "baseline",
            "section_id": "24258661-11f2-4b0d-bd15-f4ca163ad6df",
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
        "ebb88eb3-304a-4283-bfeb-fa353e486a86"
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
      "query": "?chunk_id=in.%28%22ebb88eb3-304a-4283-bfeb-fa353e486a86%22%29",
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

- Revision: `424546497abec9cf5705238c07c4a1888077759f` plus the working-tree harness/fixture changes; Python `3.14.7`, `fastapi` 0.142.2, `supabase` 2.32.0, `storage3` 2.32.0, `postgrest` 2.32.0, `httpx` 0.28.1, `pinecone` 10.0.0, `python-docx` 1.2.0; Python host `Windows`; .NET SDK 10.0.401 and Linux ASP.NET/.NET runtime 10.0.15 in `mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586`.
- All fixtures are synthetic. DOCX fixtures can be regenerated by `generate_docx_fixtures.py` with python-docx 1.2.0. Both hosts receive the same bytes and `file` field for each case.
- The report was regenerated from scratch. Two offline runs had identical normalized comparisons; original random IDs and multipart boundaries remain visible below.
- Prerequisites: Docker with the pinned image already present, .NET packages in the local NuGet cache, and the pinned Python packages in the local `uv` cache. Image/package acquisition is a separate setup step; no provider credentials are required. The commands below reproduce the Windows Git Bash/MSYS offline run from the repository root, with paths updated for the new layout. The runner fixes `PYTHONHASHSEED=0` in each Python child and performs two complete suites per invocation.

```bash
# One-time image acquisition was: docker pull mcr.microsoft.com/dotnet/aspnet:10.0
# Offline setup from already-populated caches:
uv venv --offline --python "$HOME/AppData/Roaming/uv/python/cpython-3.14-windows-x86_64-none/python.exe" "$(cygpath -aw /tmp/cfc-upload-parity-verified)"
uv pip install --offline --python "$(cygpath -aw /tmp/cfc-upload-parity-verified/Scripts/python.exe)" -r tests/DocumentUploadParity/requirements-parity.txt
empty="$(mktemp -d)"
dotnet restore tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --source "$(cygpath -w "$empty")"
rm -rf "$empty"
dotnet build tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --no-restore
/tmp/cfc-upload-parity-verified/Scripts/python.exe -m unittest discover -s tests/DocumentUploadParity -p 'test_*.py'
dotnet test tests/CFC.ChatAI.DocumentProcessor.Tests/CFC.ChatAI.DocumentProcessor.Tests.csproj --no-restore --filter FullyQualifiedName~UploadParityFixtureTests
PYTHONHASHSEED=0 /tmp/cfc-upload-parity-verified/Scripts/python.exe tests/DocumentUploadParity/run_parity.py
rm -rf /tmp/cfc-upload-parity-verified
```

Fixture hashes:

| Fixture | SHA-256 |
| --- | --- |
| `baseline.eml` | `24672c47143e5c8e8584a3f269403a066543ee6a6313d6d869d320a6d16fc092` |
| `embedded-image.docx` | `0b244c9dcd9a427e486191278d7662fe9f3a231ce0d794a638c4e5cee5a5f929` |
| `multi-section.docx` | `87a633d535c58bda3f5549277f67ca1c4142fbfdbd4ec6b4bf3437c6e086e198` |
| `unsupported.txt` | `ad4260dcfc562d35760c0427c46c08eae33d9aadac08a29a43e8becc01977bfa` |

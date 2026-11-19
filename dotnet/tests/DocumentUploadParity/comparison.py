"""Semantic comparison of sanitized upload captures; no provider access.

Only section/chunk identifiers and transport-generated multipart boundaries are
aliased. Document text, filenames, paths, array/batch order and field presence
are not normalized. Original captures are retained separately for the report.
"""
from __future__ import annotations

from dataclasses import dataclass
import json
import re
from numbers import Real
from urllib.parse import unquote


class CaptureError(ValueError):
    """Evidence is incomplete or internally inconsistent."""


class _Missing:
    pass


MISSING = _Missing()
UNOBSERVABLE = object()


def display(value):
    if value is MISSING:
        return "<MISSING>"
    if value is UNOBSERVABLE:
        return "<NOT OBSERVABLE>"
    def printable(item):
        if item is MISSING:
            return {"$missing": True}
        if isinstance(item, list):
            return [printable(v) for v in item]
        if isinstance(item, dict):
            return {k: printable(v) for k, v in item.items()}
        return item
    return json.dumps(printable(value), ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def differences(left, right, path="$"):
    """Ordered, typed JSON diff. JSON numbers compare numerically, bools do not."""
    if left is MISSING or right is MISSING:
        return [] if left is right else [(path, left, right)]
    if isinstance(left, dict) and isinstance(right, dict):
        result = []
        for key in sorted(left.keys() | right.keys()):
            result.extend(differences(left.get(key, MISSING), right.get(key, MISSING), f"{path}.{key}"))
        return result
    if isinstance(left, list) and isinstance(right, list):
        result = []
        for i in range(max(len(left), len(right))):
            result.extend(differences(left[i] if i < len(left) else MISSING,
                                      right[i] if i < len(right) else MISSING, f"{path}[{i}]"))
        return result
    if isinstance(left, Real) and not isinstance(left, bool) and isinstance(right, Real) and not isinstance(right, bool):
        return [] if left == right else [(path, left, right)]
    return [] if type(left) is type(right) and left == right else [(path, left, right)]


def section_payload(operation):
    body = operation.get("body")
    if isinstance(body, dict) and body.get("format") == "multipart":
        for part in body.get("parts", []):
            if "json" in part:
                return part["json"]
    return body if isinstance(body, dict) and "section_id" in body else None


def collect(capture):
    sections, rows, vectors = [], [], []
    for operation in capture.get("operations", []):
        if operation.get("kind") == "storage-section":
            payload = section_payload(operation)
            if not isinstance(payload, dict):
                raise CaptureError("Section request did not contain parseable JSON")
            sections.append(payload)
        if operation.get("kind") == "chunks-upsert":
            body = operation.get("body")
            if not isinstance(body, list):
                raise CaptureError("Chunk upsert body was not an array")
            rows.extend(body)
        if operation.get("kind") == "vectors-upsert":
            values = vector_batch(operation)
            if not isinstance(values, list):
                raise CaptureError("Vector capture was not an array")
            vectors.extend(values)
    return sections, rows, vectors


def validate(capture):
    """Validate original references *before* assigning any per-side aliases."""
    if capture.get("harness_error"):
        raise CaptureError("Runner failed: " + str(capture["harness_error"]))
    required = {"status", "response", "operations", "embeddings", "capture_levels", "self_test",
                "failure_requested", "failure_intercepted", "unexpected_requests"}
    if missing := required - capture.keys():
        raise CaptureError("Required capture fields missing: " + ", ".join(sorted(missing)))
    if not isinstance(capture["status"], int) or isinstance(capture["status"], bool) or not 100 <= capture["status"] <= 599:
        raise CaptureError("Endpoint status is invalid")
    if not isinstance(capture["operations"], list) or not isinstance(capture["embeddings"], list):
        raise CaptureError("Operations or embeddings are not arrays")
    levels = capture["capture_levels"]
    if not isinstance(levels, dict) or levels.get("supabase") != "wire" or levels.get("pinecone") not in {"wire", "application"}:
        raise CaptureError("Capture levels are missing or unexpected")
    if not isinstance(capture["self_test"], dict):
        raise CaptureError("Network self-test is malformed")
    for op in capture["operations"]:
        if not isinstance(op, dict) or op.get("provider") not in {"supabase", "pinecone"} or not isinstance(op.get("kind"), str):
            raise CaptureError("Operation is malformed")
        if op.get("capture") != levels[op["provider"]]:
            raise CaptureError("Operation capture level is missing or incorrect")
    tests = capture["self_test"]
    if not tests.get("socket_denied") or not tests.get("unhandled_http_denied"):
        raise CaptureError("Network-denial self-test is missing or failed")
    if not tests.get("in_process_server"):
        raise CaptureError("The in-process test host self-test is missing or failed")
    if "unexpected_network" in capture and not tests.get("dns_denied"):
        raise CaptureError("Python DNS-denial self-test is missing or failed")
    if "unexpected_network" in capture and not tests.get("dotenv_reads_blocked"):
        raise CaptureError("Python dotenv-denial self-test is missing or failed")
    if capture.get("unexpected_requests", 0) or capture.get("unexpected_network", 0):
        raise CaptureError("An unexpected request or network attempt occurred")
    failure = capture["failure_requested"]
    if failure != "none":
        failed = [op for op in capture["operations"] if op.get("kind") == failure and op.get("result") == "injected-failure"]
        if capture["failure_intercepted"] is not True or len(failed) != 1:
            raise CaptureError("Requested provider failure was not intercepted exactly once at the intended operation")
    elif capture["failure_intercepted"] or any(op.get("result") == "injected-failure" for op in capture["operations"]):
        raise CaptureError("Unexpected provider failure injection")
    sections, rows, vectors = collect(capture)
    section_ids = []
    section_operations = [op for op in capture["operations"] if op.get("kind") == "storage-section"]
    for section, operation in zip(sections, section_operations, strict=True):
        sid = section.get("section_id")
        if not isinstance(sid, str) or not sid or sid in section_ids:
            raise CaptureError("Section ID missing or duplicated")
        path = section.get("storage_path")
        if path is not None and not str(path).endswith("/" + sid + ".json"):
            raise CaptureError("Section storage path does not refer to its section ID")
        if not unquote(operation.get("path", "")).endswith("/" + sid + ".json"):
            raise CaptureError("Section request path does not refer to its section ID")
        section_ids.append(sid)
    chunk_by_id = {}
    for row in rows:
        if not isinstance(row, dict):
            raise CaptureError("Chunk row is not an object")
        cid, sid = row.get("chunk_id"), row.get("section_id")
        if not isinstance(cid, str) or not cid or cid in chunk_by_id:
            raise CaptureError("Chunk ID missing or duplicated")
        if sid is not None and (not isinstance(sid, str) or not sid):
            raise CaptureError("Invalid section reference on chunk")
        if sections and sid not in section_ids:
            raise CaptureError("Chunk refers to a section that was not captured")
        chunk_by_id[cid] = row
        if sid is not None and sid not in section_ids:
            section_ids.append(sid)  # C# does not persist section artifacts.
    vector_ids = set()
    row_positions = {row["chunk_id"]: i for i, row in enumerate(rows)}
    if vectors and len(capture["embeddings"]) != len(rows):
        raise CaptureError("Embedding count does not match chunk count")
    for vector in vectors:
        if not isinstance(vector, dict):
            raise CaptureError("Vector is not an object")
        cid = vector.get("id")
        if cid not in chunk_by_id or cid in vector_ids:
            raise CaptureError("Vector refers to an unknown or duplicate chunk")
        vector_ids.add(cid)
        metadata = vector.get("metadata")
        if not isinstance(metadata, dict) or metadata.get("section_id") != (chunk_by_id[cid].get("section_id") or ""):
            raise CaptureError("Vector section reference does not match its chunk")
        if metadata.get("doc_id") != chunk_by_id[cid].get("doc_id"):
            raise CaptureError("Vector document reference does not match its chunk")
        if not isinstance(vector.get("values"), list):
            raise CaptureError("Vector values are missing")
        embedding = capture["embeddings"][row_positions[cid]]
        if not isinstance(embedding, dict) or embedding.get("input") != chunk_by_id[cid].get("content") or embedding.get("values") != vector["values"]:
            raise CaptureError("Vector does not match its chunk's recorded embedding input and values")
    for operation in capture.get("operations", []):
        if operation.get("kind") == "vectors-delete":
            if any(cid not in chunk_by_id for cid in operation.get("ids", [])):
                raise CaptureError("Vector compensation refers to an unknown chunk")
    # Where image objects are emitted, chunk references must point at a captured
    # object path. This checks attempted operations, not remote persistence.
    image_paths = {unquote(op.get("path", "")).split("/object/parity-bucket/", 1)[-1]
                   for op in capture.get("operations", []) if op.get("kind") == "storage-image"}
    for row in rows:
        for path in row.get("image_paths") or []:
            if path not in image_paths:
                raise CaptureError("Chunk references an image without a captured storage attempt")
    return section_ids, list(chunk_by_id)


def aliases(capture):
    sections, chunks = validate(capture)
    return ({identifier: f"<section-{i}>" for i, identifier in enumerate(sections, 1)},
            {identifier: f"<chunk-{i}>" for i, identifier in enumerate(chunks, 1)})


def normalize(value, maps, key="", context=""):
    sections, chunks = maps
    if isinstance(value, dict):
        return {k: normalize(v, maps, k, key or context) for k, v in value.items()}
    if isinstance(value, list):
        return [normalize(v, maps, key, context) for v in value]
    if not isinstance(value, str):
        return value
    if key == "section_id":
        return sections.get(value, value)
    if key == "chunk_id" or (key == "id" and context == "vectors") or key == "ids":
        return chunks.get(value, value)
    if key in {"path", "storage_path", "section_path"}:
        # Replace only an actual section-object filename, not an identical
        # string appearing in a document ID or any other path component.
        value = re.sub(r"(/sections/)([^/]+)(\.json)(?=$|[?#])",
                       lambda m: m.group(1) + sections.get(m.group(2), m.group(2)) + m.group(3), value)
    if key == "content-disposition":
        for original, alias in sections.items():
            value = value.replace(f'filename="{original}.json"', f'filename="{alias}.json"')
    if key == "query":
        for original, alias in chunks.items():
            value = value.replace(f'%22{original}%22', f'%22{alias}%22')
            value = value.replace(f'"{original}"', f'"{alias}"')
    if key == "content-type":
        value = re.sub(r"(?i)(boundary=)(?:\"[^\"]+\"|[^;\s]+)", r"\1<boundary>", value)
    return value


def storage_object(op):
    """Extract logical file and multipart options from the actual HTTP body."""
    body, headers = op.get("body"), op.get("headers", {})
    result = {"kind": op["kind"], "path": op.get("path"), "x-upsert": headers.get("x-upsert", MISSING)}
    if isinstance(body, dict) and body.get("format") == "multipart":
        result["multipart_fields"] = [p for p in body["parts"] if "text" in p]
        file_parts = [p for p in body["parts"] if "binary" in p or "json" in p]
        if len(file_parts) != 1:
            raise CaptureError("Storage multipart did not contain exactly one file")
        part = file_parts[0]
        result["object_content_type"] = part["headers"].get("content-type", MISSING)
        result["object"] = part.get("binary", part.get("json"))
    else:
        result["multipart_fields"] = []
        result["object_content_type"] = headers.get("content-type", MISSING)
        result["object"] = body
    return result


def http_requests(capture, provider):
    grouped = {}
    for op in capture["operations"]:
        if op.get("provider") == provider and op.get("capture") == "wire":
            grouped.setdefault(op["kind"], []).append(
                {k: op.get(k, MISSING) for k in ("method", "origin", "path", "query", "headers", "provider_response_status", "result")})
    return grouped


def storage_objects(capture):
    grouped = {}
    for op in capture["operations"]:
        if op.get("kind", "").startswith("storage-"):
            grouped.setdefault(op["kind"], []).append(storage_object(op))
    return grouped


def vector_batch(operation):
    if operation.get("capture") == "wire":
        body = operation.get("body")
        return body.get("vectors") if isinstance(body, dict) else None
    return operation.get("vectors")


def batches(capture, kind, field):
    return [len(vector_batch(op) if kind == "vectors-upsert" else op[field])
            for op in capture["operations"] if op.get("kind") == kind]


@dataclass
class Comparison:
    differences: list
    notes: list
    normalized: dict


def compare(left, right):
    left_maps, right_maps = aliases(left), aliases(right)
    ls, lr, lv = collect(left)
    rs, rr, rv = collect(right)
    norm_l = lambda v: normalize(v, left_maps)
    norm_r = lambda v: normalize(v, right_maps)
    diffs, notes = [], []
    def check(label, a, b):
        found = differences(a, b, label)
        diffs.extend(found)
        if not found and a == [] and b == [] and label in {
            "chunk_batch_sizes", "vector_batch_sizes_at_capture_boundary", "chunk_rows", "vectors", "embedding_inputs_outputs"
        }:
            notes.append(f"{label}: neither side sent anything")
        elif not found and label == "pinecone_http" and a == {} and b == {}:
            notes.append("pinecone_http: neither side captured SDK HTTP")
        else:
            notes.append(f"{label}: {'DIFFERENT' if found else 'MATCH'}")
    check("endpoint.status", left.get("status", MISSING), right.get("status", MISSING))
    check("endpoint.response", norm_l(left.get("response", MISSING)), norm_r(right.get("response", MISSING)))
    lo = [{k: op.get(k, MISSING) for k in ("provider", "kind", "result")} for op in left["operations"]]
    ro = [{k: op.get(k, MISSING) for k in ("provider", "kind", "result")} for op in right["operations"]]
    check("ordered_operations", lo, ro)
    # Do not merge requests or conceal option/transport changes.
    check("supabase_http", norm_l(http_requests(left, "supabase")), norm_r(http_requests(right, "supabase")))
    lp, rp = norm_l(http_requests(left, "pinecone")), norm_r(http_requests(right, "pinecone"))
    if left["capture_levels"]["pinecone"] != right["capture_levels"]["pinecone"]:
        if lp or rp:
            for kind in sorted(lp.keys() | rp.keys()):
                lvalue = lp.get(kind, MISSING) if left["capture_levels"]["pinecone"] == "wire" else UNOBSERVABLE
                rvalue = rp.get(kind, MISSING) if right["capture_levels"]["pinecone"] == "wire" else UNOBSERVABLE
                diffs.append((f"pinecone_http.{kind}", lvalue, rvalue))
            notes.append("pinecone_http: NOT COMPARABLE (one side bypasses the SDK)")
        else:
            notes.append("pinecone_http: neither side captured SDK HTTP")
    else:
        check("pinecone_http", lp, rp)
    lstore, rstore = norm_l(storage_objects(left)), norm_r(storage_objects(right))
    def section_boundaries(items):
        return [(s.get("title"), s.get("level"), [b.get("type") for b in s.get("blocks", [])]) for s in items]
    if ls and rs and section_boundaries(ls) != section_boundaries(rs):
        diffs.append(("storage_sections.unmatched_boundaries", lstore.pop("storage-section"), rstore.pop("storage-section")))
        notes.append("section boundaries differ; sections shown as unmatched ordered lists")
    check("storage_objects", lstore, rstore)
    check("chunk_batch_sizes", batches(left, "chunks-upsert", "body"), batches(right, "chunks-upsert", "body"))
    check("vector_batch_sizes_at_capture_boundary", batches(left, "vectors-upsert", "vectors"), batches(right, "vectors-upsert", "vectors"))
    # If boundaries/text differ, show both ordered lists as unmatched; do not pair
    # unrelated chunks merely because they occupy the same ordinal.
    same_boundaries = len(lr) == len(rr) and [r.get("content") for r in lr] == [r.get("content") for r in rr]
    if same_boundaries:
        check("chunk_rows", norm_l(lr), norm_r(rr))
        check("vectors", normalize(lv, left_maps, key="vectors"), normalize(rv, right_maps, key="vectors"))
        check("embedding_inputs_outputs", left.get("embeddings", []), right.get("embeddings", []))
    else:
        diffs.append(("chunk_rows.unmatched_boundaries", norm_l(lr), norm_r(rr)))
        diffs.append(("vectors.unmatched_chunks", normalize(lv, left_maps, key="vectors"), normalize(rv, right_maps, key="vectors")))
        diffs.append(("embedding_inputs_outputs.unmatched_chunks", left.get("embeddings", []), right.get("embeddings", [])))
        notes.append("chunk boundaries/text differ; rows and vectors shown as unmatched ordered lists")
    python_vector_calls = [op for op in left["operations"] if op.get("kind") == "vectors-upsert"]
    if python_vector_calls:
        op = python_vector_calls[0]
        value = op.get("body", {}).get("namespace", MISSING) if op.get("capture") == "wire" else op.get("namespace", MISSING)
        notes.append(f"namespace: Python captured {display(value)}; C# is unobservable at its fake vector store")
    else:
        notes.append("namespace: Python did not call its index; C# is unobservable at its fake vector store")
    normalized = {"status": [left.get("status"), right.get("status")],
                  "differences": [(p, display(a), display(b)) for p, a, b in diffs], "notes": notes}
    return Comparison(diffs, notes, normalized)

"""Offline regression tests for the parity capture/comparison harness."""
import copy
import hashlib
import json
import unittest
from pathlib import Path
from unittest.mock import patch

import httpx

from comparison import CaptureError, MISSING, compare, differences, display, normalize, validate
from python_runner import Capture, NetworkDenied, parse_body, sanitize
import run_parity


def capture(section="section-a", chunk="chunk-a"):
    section_data = {"section_id": section, "title": "Title", "storage_path": f"docs/doc/sections/{section}.json", "blocks": [{"type": "text", "text": "hello"}]}
    row = {"chunk_id": chunk, "section_id": section, "doc_id": "doc", "section_title": "Title", "content": "hello", "image_paths": []}
    vector = {"id": chunk, "metadata": {"section_id": section, "doc_id": "doc"}, "values": [1.0, 2.0, 3.0, 4.0]}
    return {
        "status": 200, "response": {"success": True},
        "operations": [
            {"provider": "supabase", "kind": "storage-section", "capture": "wire", "method": "POST", "path": f"/storage/v1/object/parity-bucket/docs/doc/sections/{section}.json", "query": "", "headers": {"content-type": "application/json", "x-upsert": "true"}, "body": section_data, "result": "canned-success"},
            {"provider": "supabase", "kind": "chunks-upsert", "capture": "wire", "method": "POST", "path": "/rest/v1/document_chunks", "query": "", "headers": {"content-type": "application/json"}, "body": [row], "result": "canned-success"},
            {"provider": "pinecone", "kind": "vectors-upsert", "capture": "application", "vectors": [vector], "namespace": "parity-namespace", "result": "canned-success"},
        ],
        "embeddings": [{"input": "hello", "values": [1, 2, 3, 4]}],
        "capture_levels": {"supabase": "wire", "pinecone": "application"},
        "self_test": {"socket_denied": True, "unhandled_http_denied": True, "in_process_server": True},
        "failure_requested": "none", "failure_intercepted": False, "unexpected_requests": 0,
    }


class JsonComparisonTests(unittest.TestCase):
    def test_known_match_and_field_mismatch(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        self.assertEqual([], compare(a, b).differences)
        b["operations"][1]["body"][0]["section_title"] = "Different"
        paths = [item[0] for item in compare(a, b).differences]
        self.assertIn("chunk_rows[0].section_title", paths)

    def test_missing_is_not_null_and_types_are_preserved(self):
        diff = differences({"x": None}, {})
        self.assertEqual("$.x", diff[0][0])
        self.assertIsNone(diff[0][1])
        self.assertIs(diff[0][2], MISSING)
        self.assertNotEqual(display(None), display(MISSING))
        self.assertTrue(differences({"x": True}, {"x": 1}))
        self.assertFalse(differences({"x": 1}, {"x": 1.0}))  # one JSON number type
        self.assertFalse(differences({"a": 1, "b": 2}, {"b": 2, "a": 1}))
        self.assertTrue(differences([1, 2], [2, 1]))

    def test_original_ids_are_validated_before_aliasing(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        self.assertEqual([], compare(a, b).differences)
        b["operations"][2]["vectors"][0]["metadata"]["section_id"] = "wrong"
        with self.assertRaisesRegex(CaptureError, "section reference"):
            compare(a, b)
        b = capture("s2", "c2")
        b["operations"][2]["vectors"][0]["id"] = "unknown"
        with self.assertRaisesRegex(CaptureError, "unknown"):
            validate(b)
        b = capture("s2", "c2")
        b["operations"][1]["body"][0]["section_id"] = "unknown"
        with self.assertRaisesRegex(CaptureError, "not captured"):
            validate(b)
        b = capture("s2", "c2")
        b["embeddings"][0]["values"][0] = 999
        with self.assertRaisesRegex(CaptureError, "embedding input and values"):
            validate(b)

    def test_aliases_do_not_rewrite_document_path_components(self):
        maps = ({"same-id": "<section-1>"}, {"chunk-id": "<chunk-1>"})
        path = "docs/same-id/sections/same-id.json"
        self.assertEqual("docs/same-id/sections/<section-1>.json", normalize(path, maps, key="path"))
        self.assertEqual("content same-id chunk-id", normalize("content same-id chunk-id", maps, key="content"))
        query = "?chunk_id=in.%28%22chunk-id%22%29&doc_id=same-id"
        self.assertEqual("?chunk_id=in.%28%22<chunk-1>%22%29&doc_id=same-id", normalize(query, maps, key="query"))

    def test_section_boundary_mismatch_keeps_sections_unmatched(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        b["operations"][0]["body"]["title"] = "Different heading"
        result = compare(a, b)
        self.assertIn("storage_sections.unmatched_boundaries", [path for path, _, _ in result.differences])
        self.assertIn("section boundaries differ; sections shown as unmatched ordered lists", result.notes)

    def test_boundary_mismatch_keeps_unmatched_rows_and_vectors(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        b["operations"][1]["body"][0]["content"] = "different text"
        b["embeddings"][0]["input"] = "different text"
        paths = [d[0] for d in compare(a, b).differences]
        self.assertIn("chunk_rows.unmatched_boundaries", paths)
        self.assertIn("vectors.unmatched_chunks", paths)
        self.assertNotIn("chunk_rows[0].content", paths)

    def test_batch_order_and_extra_missing_operations_are_visible(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        a_row = copy.deepcopy(a["operations"][1]["body"][0]); a_row["chunk_id"] = "c1b"
        b_row = copy.deepcopy(b["operations"][1]["body"][0]); b_row["chunk_id"] = "c2b"
        a["operations"][1]["body"].append(a_row)
        b["operations"][1]["body"].append(b_row)
        a["embeddings"].append(copy.deepcopy(a["embeddings"][0]))
        b["embeddings"].append(copy.deepcopy(b["embeddings"][0]))
        extra = copy.deepcopy(b["operations"][1]); extra["body"] = [b["operations"][1]["body"].pop()]
        b["operations"].insert(2, extra)
        paths = [d[0] for d in compare(a, b).differences]
        self.assertTrue(any(p.startswith("chunk_batch_sizes") for p in paths))
        self.assertTrue(any(p.startswith("ordered_operations") for p in paths))
        b["operations"].append({"provider": "pinecone", "capture": "application", "kind": "vectors-delete", "ids": ["c2"], "result": "canned-success"})
        self.assertTrue(any(p.startswith("ordered_operations") for p, _, _ in compare(a, b).differences))

    def test_binary_size_and_hash_changes_are_visible(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        original = {"provider": "supabase", "capture": "wire", "kind": "storage-original", "method": "POST", "path": "/storage/v1/object/parity-bucket/docs/doc/original/doc.eml", "query": "", "headers": {"content-type": "message/rfc822", "x-upsert": "true"}, "body": {"size": 5, "sha256": "first"}, "result": "canned-success"}
        a["operations"].insert(0, copy.deepcopy(original))
        b["operations"].insert(0, copy.deepcopy(original))
        self.assertEqual([], compare(a, b).differences)
        b["operations"][0]["body"]["sha256"] = "second"
        paths = [p for p, _, _ in compare(a, b).differences]
        self.assertIn("storage_objects.storage-original[0].object.sha256", paths)

    def test_neither_side_sending_vectors_is_not_reported_as_match(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        a["operations"].pop(); b["operations"].pop()
        a["embeddings"] = []; b["embeddings"] = []
        notes = compare(a, b).notes
        self.assertIn("vectors: neither side sent anything", notes)
        self.assertNotIn("vectors: MATCH", notes)

    def test_serialized_python_vectors_compare_with_csharp_application_vectors(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        operation = a["operations"][2]
        vectors = operation.pop("vectors")
        namespace = operation.pop("namespace")
        operation.update({"capture": "wire", "method": "POST", "origin": "https://parity-index.invalid",
                          "path": "/vectors/upsert", "query": "", "headers": {"content-type": "application/json"},
                          "provider_response_status": 200, "body": {"vectors": vectors, "namespace": namespace}})
        a["capture_levels"]["pinecone"] = "wire"
        result = compare(a, b)
        self.assertIn("vectors: MATCH", result.notes)
        self.assertTrue(any(path.startswith("pinecone_http") for path, _, _ in result.differences))
        self.assertFalse(any(path.startswith("vectors[") for path, _, _ in result.differences))

    def test_random_ids_produce_stable_normalized_results(self):
        first = compare(capture("first-a", "chunk-a"), capture("first-b", "chunk-b")).normalized
        second = compare(capture("second-a", "chunk-c"), capture("second-b", "chunk-d")).normalized
        self.assertEqual(first, second)


class CaptureTests(unittest.TestCase):
    def test_multipart_rfc822_body_is_hashed_as_raw_bytes(self):
        raw = b"From: sender@example.invalid\r\nSubject: Test\r\n\r\nActual email payload.\r\n"
        boundary = "unit-boundary"
        body = (b"--unit-boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"x.eml\"\r\n"
                b"Content-Type: message/rfc822\r\n\r\n" + raw + b"\r\n--unit-boundary--\r\n")
        request = httpx.Request("POST", "https://provider.invalid/", headers={"content-type": "multipart/form-data; boundary=" + boundary}, content=body)
        parsed = parse_body(request, body, "storage-original")
        self.assertEqual({"size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}, parsed["parts"][0]["binary"])

    def test_provider_failure_is_intercepted_exactly_once(self):
        cap = Capture("chunks-upsert")
        def request():
            return httpx.Request("POST", "https://provider.invalid/rest/v1/document_chunks", json=[{"chunk_id": "c"}])
        self.assertEqual(503, cap.http(request()).status_code)
        self.assertTrue(cap.failure_intercepted)
        self.assertEqual("injected-failure", cap.operations[0]["result"])
        self.assertEqual(200, cap.http(request()).status_code)
        self.assertEqual("canned-success", cap.operations[1]["result"])
        vec = Capture("vectors-upsert")
        request = httpx.Request("POST", "https://parity-index.invalid/vectors/upsert", json={
            "vectors": [{"id": "c", "values": [1.0], "metadata": {"section_id": "s"}}],
            "namespace": "parity-namespace"})
        self.assertEqual(400, vec.http(request).status_code)
        self.assertTrue(vec.failure_intercepted)
        self.assertEqual("parity-namespace", vec.operations[0]["body"]["namespace"])
        self.assertEqual("wire", vec.operations[0]["capture"])

    def test_pinecone_discovery_is_canned_and_unexpected_creation_blocked(self):
        cap = Capture("none")
        listed = cap.http(httpx.Request("GET", "https://api.pinecone.io/indexes"))
        self.assertEqual("parity-index", listed.json()["indexes"][0]["name"])
        described = cap.http(httpx.Request("GET", "https://api.pinecone.io/indexes/parity-index"))
        self.assertEqual("parity-index.invalid", described.json()["host"])
        with self.assertRaises(NetworkDenied):
            cap.http(httpx.Request("POST", "https://api.pinecone.io/indexes", json={"name": "unexpected"}))
        self.assertEqual(1, cap.unexpected_requests)
        self.assertEqual(["index-list", "index-describe", "unexpected"], [op["kind"] for op in cap.operations])

    def test_secret_headers_and_dummy_credentials_are_redacted(self):
        cap = Capture("none")
        req = httpx.Request("POST", "https://provider.invalid/rest/v1/document_chunks", json=[], headers={"Authorization": "Bearer test-service", "apikey": "test-service"})
        cap.http(req)
        rendered = json.dumps(sanitize({"operations": cap.operations, "authorization": "secret", "note": "test-service"}, Path("/tmp")))
        self.assertNotIn("test-service", rendered)
        self.assertNotIn("Bearer", rendered)
        self.assertNotIn('"secret"', rendered)
        self.assertIn("<redacted>", rendered)

    def test_unhandled_request_and_socket_are_denied(self):
        cap = Capture("none")
        with self.assertRaises(NetworkDenied):
            cap.http(httpx.Request("GET", "https://provider.invalid/unhandled"))
        self.assertEqual(1, cap.unexpected_requests)
        self.assertEqual("blocked", cap.operations[0]["result"])
        with self.assertRaises(NetworkDenied):
            cap.deny_network(("192.0.2.1", 443))
        self.assertEqual(1, cap.unexpected_network)

    def test_missing_self_test_or_unintercepted_failure_is_error(self):
        cap = capture(); cap["self_test"]["socket_denied"] = False
        with self.assertRaises(CaptureError):
            validate(cap)
        cap = capture(); cap["failure_requested"] = "chunks-upsert"
        with self.assertRaisesRegex(CaptureError, "not intercepted"):
            validate(cap)
        cap["failure_intercepted"] = True  # A flag alone is not evidence of an injected request.
        with self.assertRaisesRegex(CaptureError, "not intercepted"):
            validate(cap)
        cap = capture(); del cap["operations"]
        with self.assertRaisesRegex(CaptureError, "Required capture fields"):
            validate(cap)
        case = run_parity.Case("unit", "Unit", "", "baseline.eml", "baseline.eml", "vectors-upsert")
        result = run_parity.evaluate(case, capture(), capture())
        self.assertEqual("ERROR", result["outcome"])
        self.assertTrue(any("failure target" in error for error in result["errors"]))


class ReportTests(unittest.TestCase):
    def test_regeneration_is_deterministic_and_details_not_truncated(self):
        a, b = capture("s1", "c1"), capture("s2", "c2")
        tail = "synthetic-tail-" + "x" * 25000
        a["response"]["long"] = tail
        b["response"]["long"] = tail
        comp = compare(a, b)
        results = [(case, a, b, {"outcome": "DIFFERENT", "errors": [], "comparison": comp}) for case in run_parity.CASES]
        with patch.object(run_parity, "revision", return_value="unit-test-revision"):
            first = run_parity.render(results, True)
            second = run_parity.render(results, True)
        self.assertEqual(first, second)
        for case in run_parity.CASES:
            self.assertIn("## " + case.title, first)
        self.assertIn("## Optional real-model case", first)
        self.assertEqual(2 * len(run_parity.CASES), first.count("complete sanitized capture</summary>"))
        self.assertIn(tail, first)
        self.assertIn('"values": [', first)
        run_parity.validate_report(first, results)
        with self.assertRaisesRegex(ValueError, "incomplete or truncated"):
            run_parity.validate_report(first.replace(tail, "TRUNCATED", 1), results)
        with self.assertRaisesRegex(ValueError, "malformed"):
            run_parity.validate_report(first.replace("</details>", "", 1), results)


if __name__ == "__main__":
    unittest.main()

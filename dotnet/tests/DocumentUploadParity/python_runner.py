"""One isolated Python upload capture. Invoked by run_parity.py, not a server.

Supabase and Pinecone evidence is captured at httpx.HTTPTransport, after SDK
serialization. Pinecone discovery is answered with canned responses. No network
transport is ever delegated to the real HTTP handler.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import socket
import sys
import tempfile
import threading
import types
from email import policy
from email.parser import BytesParser
from urllib.parse import unquote


class NetworkDenied(RuntimeError):
    pass


# The SDK's control-plane responses point only to this synthetic, intercepted host.
INDEX_MODEL = {
    "name": "parity-index", "host": "parity-index.invalid",
    "status": {"ready": True, "state": "Ready"}, "schema": {"fields": {}},
    "deployment": {"deployment_type": "managed", "cloud": "aws", "region": "us-east-1"},
    "deletion_protection": "disabled",
}


def scrub_environment() -> None:
    # Only OS essentials survive; credentials and proxy settings never do.
    keep = {key: os.environ[key] for key in ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP") if key in os.environ}
    os.environ.clear()
    os.environ.update(keep)
    os.environ.update({
        "SUPABASE_URL": "https://provider.invalid",
        "SUPABASE_ANON_KEY": "test-anon",
        "SUPABASE_SERVICE_ROLE_KEY": "test-service",
        "SUPABASE_BUCKET": "parity-bucket",
        "SUPABASE_BUCKET_DOCS": "parity-bucket",
        "SUPABASE_BUCKET_VIDEOS": "parity-bucket",
        "PINECONE_API_KEY": "test-pinecone",
        "PINECONE_INDEX_NAME": "parity-index",
        "PINECONE_NAMESPACE": "parity-namespace",
        "PYTHON_DOTENV_DISABLED": "1",
        "NO_PROXY": "*",
        "HF_HUB_OFFLINE": "1",
        "TRANSFORMERS_OFFLINE": "1",
    })


def binary(data: bytes) -> dict:
    return {"size": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def parse_body(request, data: bytes, kind: str):
    content_type = request.headers.get("content-type", "")
    if content_type.lower().startswith("multipart/form-data"):
        header = BytesParser(policy=policy.default).parsebytes(
            b"Content-Type: " + content_type.encode("ascii") + b"\r\n\r\n")
        boundary = header.get_boundary()
        if not boundary:
            raise RuntimeError("SDK multipart boundary is missing")
        # Parse the raw multipart bytes rather than letting the email parser
        # recursively interpret a message/rfc822 file as another message.
        segments = data.split(b"--" + boundary.encode("ascii"))
        if len(segments) < 3 or segments[0] != b"" or segments[-1] != b"--\r\n":
            raise RuntimeError("SDK multipart framing is invalid")
        parts = []
        for segment in segments[1:-1]:
            if not segment.startswith(b"\r\n") or not segment.endswith(b"\r\n"):
                raise RuntimeError("SDK multipart part framing is invalid")
            raw_headers, payload = segment[2:-2].split(b"\r\n\r\n", 1)
            part = BytesParser(policy=policy.default).parsebytes(raw_headers + b"\r\n\r\n")
            part_headers = {k.lower(): str(v) for k, v in part.items()}
            item = {"headers": part_headers}
            if part.get_filename() is None:
                item["text"] = payload.decode("utf-8")
            elif kind == "storage-section":
                item["json"] = json.loads(payload)
                item["size"] = len(payload)
                item["sha256"] = hashlib.sha256(payload).hexdigest()
            else:
                item["binary"] = binary(payload)
            parts.append(item)
        return {"format": "multipart", "parts": parts}
    if not data:
        return None
    if kind in {"chunks-upsert", "storage-section", "vectors-upsert", "vectors-delete"}:
        return json.loads(data)
    return binary(data)


class Capture:
    def __init__(self, failure):
        self.failure = failure
        self.failure_intercepted = False
        self.operations = []
        self.embeddings = []
        self.unexpected_requests = 0
        self.unexpected_network = 0
        self.self_testing = False

    def should_fail(self, kind):
        if self.failure == kind and not self.failure_intercepted:
            self.failure_intercepted = True
            return True
        return False

    def deny_network(self, *args, **kwargs):
        if not self.self_testing:
            self.unexpected_network += 1
        raise NetworkDenied("Outbound network blocked by parity runner")

    def http(self, request):
        import httpx
        raw_path = request.url.raw_path.decode("ascii")
        path, _, query = raw_path.partition("?")
        decoded = unquote(path)
        origin = f"{request.url.scheme}://{request.url.host}"
        provider = "pinecone" if origin in {"https://api.pinecone.io", "https://parity-index.invalid"} else "supabase"
        if origin == "https://provider.invalid" and request.method == "POST" and decoded.startswith("/storage/v1/object/parity-bucket/"):
            kind = ("storage-original" if "/original/" in decoded else
                    "storage-section" if "/sections/" in decoded else
                    "storage-image" if "/images/" in decoded else "unexpected")
        elif origin == "https://provider.invalid" and decoded == "/rest/v1/document_chunks" and request.method == "POST":
            kind = "chunks-upsert"
        elif origin == "https://provider.invalid" and decoded == "/rest/v1/document_chunks" and request.method == "DELETE":
            kind = "chunks-delete"
        elif origin == "https://api.pinecone.io" and request.method == "GET" and decoded == "/indexes":
            kind = "index-list"
        elif origin == "https://api.pinecone.io" and request.method == "GET" and decoded == "/indexes/parity-index":
            kind = "index-describe"
        elif origin == "https://parity-index.invalid" and request.method == "POST" and decoded == "/vectors/upsert":
            kind = "vectors-upsert"
        else:
            kind = "unexpected"
        # Allow only known non-secret request headers. Never retain auth/apikey/cookies.
        allowed = {"content-type", "content-length", "contenttype", "x-upsert", "prefer", "accept", "user-agent", "x-client-info", "x-pinecone-api-version", "x-pinecone-client-version"}
        headers = {key: value for key, value in request.headers.items() if key.lower() in allowed}
        if kind == "unexpected":
            if not self.self_testing:
                self.unexpected_requests += 1
                self.operations.append({"provider": provider, "capture": "wire", "kind": kind,
                                        "method": request.method, "origin": origin, "path": path, "query": "?" + query if query else "",
                                        "headers": headers, "body": "not read", "result": "blocked"})
            raise NetworkDenied("Unhandled HTTP request blocked")
        data = request.read()
        failed = self.should_fail(kind)
        # Pinecone receives a non-retryable 400 so an injected failure cannot
        # turn into a success on a later SDK retry. Supabase receives 503.
        status = 400 if failed and provider == "pinecone" else 503 if failed else 200
        body = parse_body(request, data, kind)
        self.operations.append({
            "provider": provider, "capture": "wire", "kind": kind,
            "method": request.method, "origin": origin, "path": path, "query": "?" + query if query else "",
            "headers": headers, "body": body, "provider_response_status": status,
            "result": "injected-failure" if failed else "canned-success",
        })
        if failed:
            return httpx.Response(status, json={"message": "injected failure", "error": "Injected", "statusCode": status}, request=request)
        if kind == "index-list":
            response = {"indexes": [INDEX_MODEL]}
        elif kind == "index-describe":
            response = INDEX_MODEL
        elif kind == "vectors-upsert":
            response = {"upsertedCount": len(body["vectors"])}
        else:
            response = {"Key": decoded.removeprefix("/storage/v1/object/")} if kind.startswith("storage-") else []
        return httpx.Response(200, json=response, request=request)


class TestEmbedding:
    def __init__(self, capture):
        self.capture = capture

    def encode(self, texts):
        vectors = []
        for text in texts:
            raw = text.encode("utf-8")
            vector = [float(len(raw)), float(sum(raw) % 997),
                      float(sum((i + 1) * value for i, value in enumerate(raw)) % 997),
                      float(raw.count(32))]
            self.capture.embeddings.append({"input": text, "values": vector})
            vectors.append(vector)
        return vectors


def sanitize(value, root):
    if isinstance(value, str):
        for secret in ("test-service", "test-anon", "test-pinecone"):
            value = value.replace(secret, "<redacted>")
        return value.replace(str(root), "<documents>")
    if isinstance(value, list):
        return [sanitize(v, root) for v in value]
    if isinstance(value, dict):
        sensitive = {"authorization", "apikey", "api-key", "x-api-key", "cookie", "set-cookie"}
        return {k: "<redacted>" if k.lower() in sensitive else sanitize(v, root) for k, v in value.items()}
    return value


def run(fixture, filename, failure):
    scrub_environment()
    import dotenv
    # Both Supabase modules import this name during module initialization. The
    # stub records attempted loads but never opens the repository's .env.
    dotenv_attempts = []
    def disable_dotenv(*args, **kwargs):
        dotenv_attempts.append(True)
        return False
    dotenv.load_dotenv = disable_dotenv
    # Avoid importing/downloading model packages; ingest's real EmbeddingModel is
    # replaced after import with the deterministic test embedder below.
    stub = types.ModuleType("sentence_transformers")
    stub.SentenceTransformer = type("UnusedSentenceTransformer", (), {})
    sys.modules["sentence_transformers"] = stub

    capture = Capture(failure)
    # Windows implements socketpair using a private loopback listener. Permit
    # only the connect performed inside that stdlib implementation, not normal
    # loopback connections or any DNS/outbound connection.
    original_pair, original_connect = socket.socketpair, socket.socket.connect
    local = threading.local()
    def guarded_pair(*args, **kwargs):
        local.in_socketpair = True
        try:
            return original_pair(*args, **kwargs)
        finally:
            local.in_socketpair = False
    def guarded_connect(sock, address):
        if getattr(local, "in_socketpair", False):
            return original_connect(sock, address)
        return capture.deny_network(sock, address)
    socket.socketpair = guarded_pair
    socket.socket.connect = guarded_connect
    socket.socket.connect_ex = capture.deny_network
    socket.getaddrinfo = capture.deny_network
    import httpx
    httpx.HTTPTransport.handle_request = lambda self, request: capture.http(request)
    async def async_http(self, request):
        return capture.http(request)
    httpx.AsyncHTTPTransport.handle_async_request = async_http

    capture.self_testing = True
    try:
        try:
            with socket.socket() as probe:
                probe.connect(("192.0.2.1", 443))
            socket_denied = False
        except NetworkDenied:
            socket_denied = True
        try:
            socket.getaddrinfo("provider.invalid", 443)
            dns_denied = False
        except NetworkDenied:
            dns_denied = True
        try:
            # Exercise the patched HTTP transport itself, not just the handler.
            with httpx.Client(trust_env=False) as probe:
                probe.get("https://provider.invalid/unhandled-self-test")
            unknown_denied = False
        except NetworkDenied:
            unknown_denied = True
    finally:
        capture.self_testing = False
    if not socket_denied or not dns_denied or not unknown_denied:
        raise RuntimeError("Network-denial self-test failed")

    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from starlette.testclient import _TestClientTransport
    from app.config import settings
    with tempfile.TemporaryDirectory(prefix="upload-parity-documents-") as tmp:
        root = Path(tmp)
        settings.DOCUMENTS_DIR = root
        settings.DATA_DIR = root
        from app.api.endpoints import ingest, upload
        if len(dotenv_attempts) < 2:
            raise RuntimeError("Expected dotenv load attempts were not intercepted")
        if (ingest._vector_store.namespace != "parity-namespace" or
                ingest._content_repository.doc_bucket != "parity-bucket" or
                upload.SUPABASE_BUCKET != "parity-bucket" or upload.SUPABASE_URL != "https://provider.invalid"):
            raise RuntimeError("Test provider settings did not reach the application")
        ingest._embedding_model = TestEmbedding(capture)
        app = FastAPI()
        app.include_router(upload.router, prefix="/api/files")
        with TestClient(app, raise_server_exceptions=False) as client:
            if not isinstance(client._transport, _TestClientTransport):
                raise RuntimeError("The upload must use an in-process TestClient transport")
            response = client.post("/api/files/upload", files={"file": (filename, Path(fixture).read_bytes(), "application/octet-stream")})
        try:
            body = response.json()
        except ValueError:
            body = response.text
        result = {
            "status": response.status_code, "response": body,
            "operations": capture.operations, "embeddings": capture.embeddings,
            "capture_levels": {"supabase": "wire", "pinecone": "wire"},
            "namespace_configured": ingest._vector_store.namespace,
            "namespace_observed": "serialized in Pinecone upsert JSON when present",
            "self_test": {"socket_denied": socket_denied, "dns_denied": dns_denied, "unhandled_http_denied": unknown_denied,
                          "in_process_server": True, "dotenv_reads_blocked": True},
            "failure_requested": failure, "failure_intercepted": capture.failure_intercepted,
            "unexpected_requests": capture.unexpected_requests, "unexpected_network": capture.unexpected_network,
        }
        result = sanitize(result, root)
    if capture.unexpected_requests or capture.unexpected_network or (failure != "none" and not capture.failure_intercepted):
        result["harness_error"] = "CaptureInvariantFailed"
        result["detail"] = "Unexpected traffic or the requested failure was not intercepted."
    return result


def main():
    if len(sys.argv) != 5:
        raise SystemExit("Expected fixture path, filename, failure kind, output path")
    try:
        result = run(sys.argv[1], sys.argv[2], sys.argv[3])
        Path(sys.argv[4]).write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
        if "harness_error" in result:
            raise SystemExit(2)
    except Exception as exc:
        Path(sys.argv[4]).write_text(json.dumps({"harness_error": type(exc).__name__, "detail": str(exc)}, indent=2), encoding="utf-8")
        raise


if __name__ == "__main__":
    main()

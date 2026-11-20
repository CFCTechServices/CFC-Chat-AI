"""Opt-in smoke test for a localhost .NET host backed only by disposable providers."""

import argparse
import json
import os
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener, urlopen
from uuid import UUID, uuid4


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def api(method, path, expected, token=None, body=None, content_type=None):
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    if isinstance(body, dict):
        body = json.dumps(body).encode()
        content_type = "application/json"
    if content_type:
        headers["Content-Type"] = content_type
    try:
        with build_opener(ProxyHandler({}), NoRedirect()).open(Request(BASE + path, body, headers, method=method), timeout=60) as res:
            status, payload = res.status, res.read()
    except HTTPError as error:
        status, payload = error.code, b""
    if status != expected:
        raise RuntimeError(f"{method} {path}: expected {expected}, got {status}")
    return json.loads(payload) if payload else None


def multipart(name, content):
    boundary = uuid4().hex
    return (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\n\r\n'.encode()
            + content + f"\r\n--{boundary}--\r\n".encode()), f"multipart/form-data; boundary={boundary}"


def check_download(route, token, expected):
    signed = api("GET", route + "/download", 200, token)["url"]
    # No API header is passed to Storage. Avoid printing signed URLs, even for failures.
    try:
        with urlopen(signed, timeout=30) as res:
            content = res.read(len(expected) + 1)
    except Exception:
        raise RuntimeError("Signed original download failed") from None
    assert content == expected, "Downloaded bytes differ"


def documents(root, admin, user):
    doc_id = "cfc-smoke-" + uuid4().hex
    name = doc_id + ".txt"
    staged = root / name
    initial = b"Initial disposable internal API smoke document."
    replacement = b"Updated disposable internal API smoke document."
    route = "/api/admin/documents/" + doc_id
    with staged.open("xb") as file:
        file.write(initial)
    print(f"Disposable document ID: {doc_id}")
    try:
        stats = api("GET", "/api/admin/ingestion/stats", 200, admin)
        assert {"name": name, "size": len(initial), "status": "pending"} in stats["documents"]
        api("POST", "/api/ingest/document", 403, user, {"filename": name})
        posted = api("POST", "/api/ingest/document", 200, admin, {"filename": name})
        assert posted["success"] and posted["doc_id"] == doc_id and posted["source"] == name
        api("POST", "/api/ingest/document", 409, admin, {"filename": name})
        check_download(route, admin, initial)
        api("PUT", route + "/replace", 409, admin, *multipart(doc_id + "-other.txt", replacement))
        check_download(route, admin, initial)
        updated = api("PUT", route + "/replace", 200, admin, *multipart(name, replacement))
        assert updated["success"] and updated["ingestion"]["doc_id"] == doc_id
        check_download(route, admin, replacement)
        assert api("DELETE", route, 200, admin)["success"]
        api("GET", route + "/download", 404, admin)
        api("DELETE", route, 404, admin)
    finally:
        staged.unlink()
    print("Staging, replacement, download and deletion API checks passed.")


def account(target, admin, remove):
    # Use a separate disposable target, never the admin or normal-user probe.
    if str(target) == api("GET", "/api/profile/me", 200, admin)["id"]:
        raise RuntimeError("Target must be separate from the admin")
    path = f"/api/admin/users/{target}"
    token = os.environ["SMOKE_ACCOUNT_TARGET_TOKEN"]
    if str(target) != api("GET", "/api/profile/me", 200, token)["id"]:
        raise RuntimeError("Target token does not match target ID")
    deactivated = api("POST", path + "/deactivate", 200, admin, {"reason": "disposable smoke"})
    assert deactivated["status"] == "inactive" and deactivated["deleted_at"]
    try:
        api("GET", "/api/profile/me", 403, token)
    except RuntimeError:
        api("GET", "/api/profile/me", 401, token)  # Auth may reject the revoked token first.
    restored = api("POST", path + "/reactivate", 200, admin)
    assert restored["status"] == "active" and restored["restored_at"]
    if remove:
        assert api("DELETE", path, 200, admin)["permanently_deleted"]
        api("DELETE", path, 404, admin)
    print("Disposable account lifecycle API checks passed.")


def main():
    global BASE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--confirm-disposable", action="store_true", required=True)
    parser.add_argument("--url", default="http://127.0.0.1:5000")
    parser.add_argument("--staging-root", type=Path, required=True)
    parser.add_argument("--account-target", type=UUID)
    parser.add_argument("--delete-account-target", action="store_true")
    args = parser.parse_args()
    origin = urlsplit(args.url)
    if (origin.scheme != "http" or origin.hostname not in ("127.0.0.1", "::1") or origin.username is not None
            or origin.path not in ("", "/") or origin.query or origin.fragment):
        parser.error("URL must be a literal HTTP loopback origin")
    if args.delete_account_target and not args.account_target:
        parser.error("--delete-account-target requires --account-target")
    BASE = args.url.rstrip("/")
    admin, user = os.environ["SMOKE_ADMIN_TOKEN"], os.environ["SMOKE_USER_TOKEN"]
    api("GET", "/api/admin/ingestion/stats", 401)
    api("GET", "/api/admin/ingestion/stats", 403, user)
    api("PATCH", "/api/profile/me", 405, user, {"full_name": "disabled"})
    api("POST", "/api/profile/complete", 404, user, {"full_name": "disabled"})
    if args.account_target and str(args.account_target) == api("GET", "/api/profile/me", 200, user)["id"]:
        parser.error("Account target must be separate from the normal-user probe")
    documents(args.staging_root, admin, user)
    if args.account_target:
        account(args.account_target, admin, args.delete_account_target)


if __name__ == "__main__":
    main()

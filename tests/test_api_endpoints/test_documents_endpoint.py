"""
Tests for GET /api/admin/documents endpoint.

The key invariant being verified:

    SELECT count(distinct doc_id) FROM public.document_chunks;

…must equal the number of document cards that the UI renders.

The UI (web/components/admin/content.jsx) calls GET /api/admin/documents and
renders one card for every entry in ``response.json()["documents"]``, so we
only need to assert:

    len(response.json()["documents"]) == distinct_doc_id_count_from_db
"""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.endpoints.admin import documents as docs_module
from app.core.auth import get_current_admin


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_chunk_rows(*doc_ids: str) -> list[dict]:
    """
    Build a flat list of ``document_chunks`` rows, with each doc_id appearing
    multiple times to simulate a realistic table that contains many chunks per
    document.
    """
    rows = []
    for doc_id in doc_ids:
        for chunk_num in range(1, 4):  # 3 chunks per document
            rows.append(
                {
                    "doc_id": doc_id,
                    "source": f"docs/{doc_id}/original/{doc_id}.docx",
                    "source_type": "document",
                }
            )
    return rows


def _distinct_doc_id_count(rows: list[dict]) -> int:
    """Equivalent of ``SELECT count(distinct doc_id) FROM document_chunks``."""
    return len({row["doc_id"] for row in rows if row.get("doc_id")})


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture()
def client(monkeypatch):
    """
    A TestClient wired to the admin documents router, with all external
    dependencies (Supabase, VectorStore, auth) patched out.

    FastAPI dependency overrides are used to bypass ``get_current_admin``
    without needing a real JWT — matching the pattern used throughout this
    test suite.
    """
    app = FastAPI()
    app.include_router(docs_module.router, prefix="/api/admin")

    # Override the admin-auth dependency so the endpoint is reachable
    # without real credentials.
    app.dependency_overrides[get_current_admin] = lambda: {"id": "test-admin"}

    return TestClient(app)


def _patch_supabase(monkeypatch, rows: list[dict]) -> None:
    """Replace the module-level ``supabase`` client in documents.py with a
    lightweight fake that returns ``rows`` for any ``.select().execute()``
    chain."""

    class _FakeResult:
        data = rows

    class _FakeQuery:
        def select(self, *_):
            return self

        def execute(self):
            return _FakeResult()

    class _FakeSupabase:
        def table(self, _name):
            return _FakeQuery()

    monkeypatch.setattr(docs_module, "supabase", _FakeSupabase())


# ---------------------------------------------------------------------------
# The invariant test
# ---------------------------------------------------------------------------

@pytest.mark.parametrize(
    "doc_ids",
    [
        pytest.param(
            ["beef-nutrition", "chicken-nutrition", "pork-nutrition"],
            id="three_unique_documents",
        ),
        pytest.param(
            ["single-doc"],
            id="one_unique_document",
        ),
        pytest.param(
            [],
            id="empty_database",
        ),
    ],
)
def test_ui_document_count_matches_distinct_doc_id_count_in_db(
    client, monkeypatch, doc_ids
):
    """
    Core invariant: the number of document cards shown in the UI must equal
    ``SELECT count(distinct doc_id) FROM public.document_chunks``.

    The UI (content.jsx) renders one card per entry in the API response's
    ``documents`` list, so we assert:

        len(response["documents"]) == count(distinct doc_id)

    This ensures the Content Library tab stays in sync with the database.
    """
    rows = _make_chunk_rows(*doc_ids)

    # Simulate what the DB query returns: all raw chunk rows
    _patch_supabase(monkeypatch, rows)

    # The Python equivalent of: SELECT count(distinct doc_id) FROM document_chunks
    expected_distinct_count = _distinct_doc_id_count(rows)

    response = client.get("/api/admin/documents")

    assert response.status_code == 200, (
        f"Expected 200 but got {response.status_code}: {response.text}"
    )

    body = response.json()

    # The UI renders one card per element in body["documents"]
    ui_document_count = len(body["documents"])

    assert ui_document_count == expected_distinct_count, (
        f"UI would render {ui_document_count} document card(s), but the DB "
        f"contains {expected_distinct_count} distinct doc_id(s). "
        f"The Content Library tab will be out of sync with the database."
    )

    # Also verify the ``total`` field exposed by the API matches.
    assert body["total"] == expected_distinct_count, (
        f"API total field reported {body['total']} but DB has "
        f"{expected_distinct_count} distinct doc_id(s)."
    )

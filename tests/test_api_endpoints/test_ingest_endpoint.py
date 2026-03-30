from pathlib import Path
import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient
from app.api.endpoints import ingest
from app.config import settings

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(ingest.router)
    return TestClient(app)

@pytest.fixture()
def temp_dirs(tmp_path, monkeypatch):
    documents_dir = tmp_path / "documents"
    data_dir = tmp_path / "data"
    documents_dir.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)

    monkeypatch.setattr(settings, "DOCUMENTS_DIR", documents_dir, raising=False)
    monkeypatch.setattr(settings, "DATA_DIR", data_dir, raising=False)

    return {
        "documents": documents_dir,
        "data": data_dir,
    }


def test_ingest_document_returns_expected_payload(client, monkeypatch, tmp_path):
    """Test that ingesting a single document returns the expected response payload and calls vector store upsert with correct vectors."""
    file_path = tmp_path / "beef-nutrition.docx"
    vectors_upserted = []

    monkeypatch.setattr(ingest, "_locate_document", lambda _filename: file_path)
    monkeypatch.setattr(
        ingest._document_processor,
        "process_document",
        lambda _path: {"success": True, "doc_id": "doc-1"},
    )
    monkeypatch.setattr(
        ingest,
        "_persist_document_content",
        lambda _processed: {
            "doc_id": "doc-1",
            "section_count": 2,
            "image_count": 1,
            "chunks": [{"chunk_id": "chunk-1"}, {"chunk_id": "chunk-2"}],
        },
    )
    monkeypatch.setattr(
        ingest,
        "_prepare_vectors",
        lambda _updated: (
            [("chunk-1", [0.1, 0.2], {"doc_id": "doc-1"}), ("chunk-2", [0.3, 0.4], {"doc_id": "doc-1"})],
            2,
        ),
    )
    monkeypatch.setattr(ingest._vector_store, "upsert_vectors", lambda vectors: vectors_upserted.append(vectors))

    response = client.post("/ingest/document", json={"filename": "beef-nutrition.docx"})

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "Successfully ingested beef-nutrition.docx",
        "chunks_processed": 2,
        "sections_processed": 2,
        "images_processed": 1,
        "doc_id": "doc-1",
        "source": str(file_path),
        "error": None,
    }
    assert len(vectors_upserted) == 1
    assert len(vectors_upserted[0]) == 2


def test_ingest_document_when_processing_fails(client, monkeypatch, tmp_path):
    """Test that ingesting a document returns a 400 error when processing fails."""
    monkeypatch.setattr(ingest, "_locate_document", lambda _filename: tmp_path / "beefnutrition.txt")
    monkeypatch.setattr(
        ingest._document_processor,
        "process_document",
        lambda _path: {"success": False, "error": "Unknown processing error"},
    )

    response = client.post("/ingest/document", json={"filename": "beefnutrition.txt"})

    assert response.status_code == 400
    assert response.json() == {"detail": "Unknown processing error"}


def test_ingest_document_on_unexpected_exception(client, monkeypatch, tmp_path):
    """Test that ingesting a document returns a 500 error when an unexpected exception occurs."""

    monkeypatch.setattr(ingest, "_locate_document", lambda _filename: tmp_path / "beefnutrition.txt")

    def raise_error(_path):
        raise RuntimeError("Cannot process document")

    monkeypatch.setattr(ingest._document_processor, "process_document", raise_error)

    response = client.post("/ingest/document", json={"filename": "beefnutrition.txt"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Cannot process document"}


def test_ingest_document_missing_filename(client):
    """Test that ingesting a document without providing a filename returns a 422 error."""
    response = client.post("/ingest/document", json={})
    assert response.status_code == 422



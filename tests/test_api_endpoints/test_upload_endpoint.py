from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.config import settings
import pytest
from app.api.endpoints import upload

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(upload.router)
    return TestClient(app)

SUPABASE_URL = "https://test.supabase.co"
SUPABASE_SERVICE_ROLE_KEY = "service-role-key"
SUPABASE_BUCKET = "cfc-docs"

@pytest.fixture()
def mock_settings(monkeypatch):
    monkeypatch.setattr(settings, "SUPABASE_URL", SUPABASE_URL, raising=False)
    monkeypatch.setattr(settings, "SUPABASE_SERVICE_ROLE_KEY", SUPABASE_SERVICE_ROLE_KEY, raising=False)
    monkeypatch.setattr(settings, "SUPABASE_BUCKET", SUPABASE_BUCKET, raising=False)
    # Patch module-level globals that the endpoint reads at import time via os.getenv()
    monkeypatch.setattr(upload, "SUPABASE_URL", SUPABASE_URL)
    monkeypatch.setattr(upload, "SUPABASE_SERVICE_ROLE_KEY", SUPABASE_SERVICE_ROLE_KEY)
    monkeypatch.setattr(upload, "SUPABASE_BUCKET", SUPABASE_BUCKET)

@pytest.fixture()
def temp_documents_dir(tmp_path, monkeypatch):
    document_dir = tmp_path / "documents"
    document_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setattr(settings, "DOCUMENTS_DIR", document_dir, raising=False)
    return document_dir

class FakeSupabaseStorage:
    def __init__(self):
        self.files = {}


class FakeUploadResult:
    def __init__(self, path):
        self.path = path


def make_fake_supabase_client(fake_supabase, captured=None, upload_error=None):
    if captured is None:
        captured = {}

    def create_client(url, key):
        captured["url"] = url
        captured["key"] = key

        class FakeStorage:
            def from_(self, bucket):
                captured["bucket"] = bucket

                class FakeBucket:
                    def upload(self, path, content, options=None):
                        if upload_error is not None:
                            raise upload_error
                        fake_supabase.files[path] = content
                        captured["options"] = options
                        return FakeUploadResult(path)

                return FakeBucket()

        class FakeClient:
            @property
            def storage(self):
                return FakeStorage()

        return FakeClient()

    return create_client


def make_fake_ingest(success=True, error_message="ingest failed"):
    calls = []

    async def fake_ingest(request):
        calls.append(request.filename)
        if not success:
            raise RuntimeError(error_message)

        class FakeIngestResult:
            def model_dump(self):
                return {"success": True, "filename": request.filename}

        return FakeIngestResult()

    return fake_ingest, calls


def test_upload_single_file_returns_expected_response(client, temp_documents_dir, mock_settings, monkeypatch):
    """Test that uploading a single file works end-to-end."""
    fake_supabase = FakeSupabaseStorage()
    captured = {}
    mock_ingest_document, ingest_calls = make_fake_ingest(success=True)

    monkeypatch.setattr(upload, "create_client", make_fake_supabase_client(fake_supabase, captured=captured))
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)

    test_filename = "beef nutrition.txt"
    test_content = b"This is beef nutrition information."

    response = client.post(
        "/upload",
        files={"file": (test_filename, test_content, "text/plain")},
    )

    assert response.status_code == 200

    expected_storage_path = f"docs/beef-nutrition/original/{test_filename}"
    assert expected_storage_path in fake_supabase.files
    assert fake_supabase.files[expected_storage_path] == test_content

    body = response.json()
    assert body["message"] == "File uploaded and ingestion triggered"
    assert body["supabase"] == {"path": expected_storage_path}
    assert body["ingestion"] == {"success": True, "filename": test_filename}

    assert captured["url"] == SUPABASE_URL
    assert captured["key"] == SUPABASE_SERVICE_ROLE_KEY
    assert captured["bucket"] == SUPABASE_BUCKET
    assert captured["options"] == {"upsert": "true"}
    assert ingest_calls == [test_filename]

def test_upload_file_with_no_file(client):
    # Test that uploading with no file returns 422
    response = client.post("/upload", data={})
    assert response.status_code == 422
    detail = response.json()["detail"]
    assert isinstance(detail, list)
    assert detail[0]["loc"] == ["body", "file"]
    assert detail[0]["type"] == "missing"
    
def test_upload_file_with_no_filename(client):
    """Test that uploading a file with no filename returns 422."""
    response = client.post(
        "/upload",
        files={"file": ("", b"content", "text/plain")},
    )
    assert response.status_code == 422
    detail = response.json()["detail"]
    assert isinstance(detail, list)
    assert detail[0]["loc"] == ["body", "file"]
    assert detail[0]["type"] == "value_error"

def test_upload_file_with_invalid_extension(client):
    """Test that uploading a file with an invalid extension returns 400."""
    response = client.post(
        "/upload",
        files={"file": ("beefnutrition.exe", b"content", "application/octet-stream")},
    )
    assert response.status_code == 400
    assert response.json() == {"detail": "Unsupported file type: .exe"}

def test_upload_file_supabase_failure_returns_success_with_error_payload(
    client, temp_documents_dir, mock_settings, monkeypatch
):
    """Test that a Supabase upload failure will not affect the uploading process and returns success with error info."""  
    fake_supabase = FakeSupabaseStorage()
    mock_ingest_document, ingest_calls = make_fake_ingest(success=True)

    monkeypatch.setattr(
        upload,
        "create_client",
        make_fake_supabase_client(
            fake_supabase,
            upload_error=RuntimeError("Supabase upload failed"),
        ),
    )
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)

    test_filename = "Beef nutrition.txt"
    test_content = b"This is beef nutrition information."

    response = client.post(
        "/upload",
        files={"file": (test_filename, test_content, "text/plain")},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["message"] == "File uploaded and ingestion triggered"
    assert "error" in body["supabase"]
    assert body["supabase"]["error"] == "Supabase upload failed"
    assert body["ingestion"] == {"success": True, "filename": test_filename}
    assert ingest_calls == [test_filename]


def test_upload_file_ingest_failure_returns_success_with_ingestion_error(
    client, temp_documents_dir, mock_settings, monkeypatch
):
    """Test that an ingestion failure will not affect the uploading process and returns success with error info."""
    fake_supabase = FakeSupabaseStorage()
    mock_ingest_document, ingest_calls = make_fake_ingest(success=False, error_message="ingest failed")

    monkeypatch.setattr(upload, "create_client", make_fake_supabase_client(fake_supabase))
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)

    test_filename = "beefnutrition.txt"
    test_content = b"This is beef nutrition information."
    response = client.post("/upload", files={"file": (test_filename, test_content, "text/plain")})

    assert response.status_code == 200
    body = response.json()
    assert body["supabase"]["path"] == f"docs/beefnutrition/original/{test_filename}"
    assert body["ingestion"] == {"success": False, "error": "ingest failed"}
    assert ingest_calls == [test_filename]


def test_bulk_upload_returns_expected_payload(client, temp_documents_dir, mock_settings, monkeypatch):
    """Test that bulk uploading multiple files works end-to-end."""
    fake_supabase = FakeSupabaseStorage()
    mock_ingest_document, ingest_calls = make_fake_ingest(success=True)

    monkeypatch.setattr(upload, "create_client", make_fake_supabase_client(fake_supabase))
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)
    

    response = client.post(
        "/bulk",
        files=[
            ("files", ("beefnutrition.txt", b"This is beef nutrition information.", "text/plain")),
            ("files", ("chickennutrition.txt", b"This is chicken nutrition information.", "text/plain")),
        ],
    )

    assert response.status_code == 200
    body = response.json()
    assert body["message"] == "Bulk upload completed"
    assert body["total"] == 2
    assert body["successful"] == 2
    assert body["failed"] == 0
    assert len(body["results"]) == 2

    first_file = body["results"][0]
    second_file = body["results"][1]
    assert first_file["filename"] == "beefnutrition.txt"
    assert second_file["filename"] == "chickennutrition.txt"
    assert first_file["ingestion"] == {"success": True, "filename": "beefnutrition.txt"}
    assert second_file["ingestion"] == {"success": True, "filename": "chickennutrition.txt"}
    assert ingest_calls == ["beefnutrition.txt", "chickennutrition.txt"]

    assert fake_supabase.files["docs/beefnutrition/original/beefnutrition.txt"] == b"This is beef nutrition information."
    assert fake_supabase.files["docs/chickennutrition/original/chickennutrition.txt"] == b"This is chicken nutrition information."

def test_bulk_upload_supabase_failure_returns_success_with_supabase_error(
    client, temp_documents_dir, mock_settings, monkeypatch
):
    """Test that a Supabase upload failure during bulk upload will not affect the uploading process and returns success with error info."""
    fake_supabase = FakeSupabaseStorage()

    monkeypatch.setattr(
        upload,
        "create_client",
        make_fake_supabase_client(
            fake_supabase,
            upload_error=RuntimeError("Supabase upload failed"),
        ),
    )
    mock_ingest_document, ingest_calls = make_fake_ingest(success=True)
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)

    response = client.post(
        "/bulk",
        files=[
            ("files", ("beefnutrition.txt", b"This is beef nutrition information.", "text/plain")),
            ("files", ("chickennutrition.txt", b"This is chicken nutrition information.", "text/plain")),
        ],
    )

    assert response.status_code == 200
    body = response.json()
    assert body["total"] == 2
    assert body["successful"] == 2
    assert body["failed"] == 0

    for result in body["results"]:
        assert "error" in result["supabase"]
        assert result["supabase"]["error"] == "Supabase upload failed"
        assert result["ingestion"] == {"success": True, "filename": result["filename"]}
def test_bulk_upload_handles_ingest_failure_returns_success_with_ingest_error(client, temp_documents_dir, mock_settings, monkeypatch):
    """Test that an ingestion failure will not affect the uploading process and returns success with error info."""
    fake_supabase = FakeSupabaseStorage()
    async def mock_ingest_document(request):
        if request.filename == "car.txt":
            raise RuntimeError("ingest failed for car information")

        """model_dump in ingest part has to be returned as an object"""
        class FakeResult:
            def model_dump(self):
                return {"success": True, "filename": request.filename}

        return FakeResult()

    monkeypatch.setattr(upload, "create_client", make_fake_supabase_client(fake_supabase))
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)

    response = client.post(
        "/bulk",
        files=[
            ("files", ("beefnutrition.txt", b"This is beef nutrition information.", "text/plain")),
            ("files", ("car.txt", b"This is car information.", "text/plain")),
        ],
    )

    assert response.status_code == 200
    body = response.json()
    assert body["total"] == 2
    assert body["successful"] == 1
    assert body["failed"] == 1

    results_by_name = {item["filename"]: item for item in body["results"]}
    assert results_by_name["beefnutrition.txt"]["ingestion"] == {"success": True, "filename": "beefnutrition.txt"}
    assert results_by_name["car.txt"]["ingestion"] == {
        "success": False,
        "error": "ingest failed for car information",
    }

def test_bulk_upload_with_missing_filename_per_file(client, temp_documents_dir, mock_settings, monkeypatch):
    """Test that bulk file do not allow invalid files uploading"""
    fake_supabase = FakeSupabaseStorage()
    mock_ingest_document, ingest_calls = make_fake_ingest(success=True)

    monkeypatch.setattr(upload, "create_client", make_fake_supabase_client(fake_supabase))
    monkeypatch.setattr(upload, "ingest_document", mock_ingest_document)
    response = client.post(
        "/bulk",
        files=[
            ("files", ("beefnutrition.txt", b"This is beef nutrition information.", "text/plain")),
            ("files", ("", b"This file has no filename.", "text/plain")),
        ],
    )
    assert response.status_code == 422
    
def test_bulk_upload_missing_files_returns_422(client):
    """Test that bulk uploading with no files returns 422."""
    response = client.post("/bulk", data={})
    assert response.status_code == 422




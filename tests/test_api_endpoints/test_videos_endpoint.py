from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from app.api.endpoints import videos

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(videos.router)
    return TestClient(app)


def test_upload_video_returns_expected_payload(client, monkeypatch):
    upload_calls = []

    def fake_upload_bytes(bucket, storage_path, data, content_type=None):
        upload_calls.append(storage_path)
        return f"https://cdn.example.com/{storage_path}"

    def fake_transcribe_to_segments(tmp_video_path, model_name, language):
        return [
            {"start": 0.0, "end": 2.0, "text": "Beef nutrition starts here."},
            {"start": 2.0, "end": 4.0, "text": "Protein and minerals are discussed."},
        ]

    monkeypatch.setattr(videos, "_bucket_name", lambda: "cfc-videos-test")
    monkeypatch.setattr(videos, "_upload_bytes", fake_upload_bytes)
    monkeypatch.setattr(videos, "_transcribe_to_segments", fake_transcribe_to_segments)
    monkeypatch.setattr(videos, "_index_transcript_chunks", lambda *_args, **_kwargs: 2)

    response = client.post(
        "/api/videos/upload",
        data={"slug": "Beef Clip", "model": "small"},
        files={"file": ("beef-clip.mp4", b"fake-video-bytes", "video/mp4")},
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload == {
        "ok": True,
        "slug": "beef-clip",
        "original_video_url": "https://cdn.example.com/videos/beef-clip/original/beef-clip.mp4",
        "transcripts": {
            "txt": "https://cdn.example.com/videos/beef-clip/transcripts/beef-clip.txt",
            "srt": "https://cdn.example.com/videos/beef-clip/transcripts/beef-clip.srt",
            "vtt": "https://cdn.example.com/videos/beef-clip/transcripts/beef-clip.vtt",
        },
        "summary_md": "https://cdn.example.com/videos/beef-clip/summary/beef-clip.md",
    }
    assert upload_calls == [
        "videos/beef-clip/original/beef-clip.mp4",
        "videos/beef-clip/transcripts/beef-clip.txt",
        "videos/beef-clip/transcripts/beef-clip.srt",
        "videos/beef-clip/transcripts/beef-clip.vtt",
        "videos/beef-clip/summary/beef-clip.md",
    ]


def test_upload_video_rejects_invalid_slug(client):
    response = client.post(
        "/api/videos/upload",
        data={"slug": "invalid/slug", "model": "small"},
        files={"file": ("beef-clip.mp4", b"fake-video-bytes", "video/mp4")},
    )

    assert response.status_code == 400
    assert response.json() == {"detail": "slug must be non-empty and cannot contain '/'"}
    
def test_upload_video_rejects_missing_slug(client):
    response = client.post(
        "/api/videos/upload",
        data={"model": "small"},
        files={"file": ("beef-clip.mp4", b"fake-video-bytes", "video/mp4")},
    )

    assert response.status_code == 422
    detail = response.json()["detail"]
    assert isinstance(detail, list)
    assert detail[0]["loc"] == ["body", "slug"]
    assert detail[0]["type"] == "missing"

def test_upload_video_rejects_empty_file(client):
    response = client.post(
        "/api/videos/upload",
        data={"slug": "beef-clip", "model": "small"},
        files={"file": ("beef-clip.mp4", b"", "video/mp4")},
    )

    assert response.status_code == 400
    assert response.json() == {"detail": "empty file"}


def test_upload_video_returns_500_on_transcription_error(client, monkeypatch):
    monkeypatch.setattr(videos, "_bucket_name", lambda: "cfc-videos-test")
    monkeypatch.setattr(videos, "_upload_bytes", lambda *_args, **_kwargs: "https://cdn.example.com/videos/file.mp4")

    def fail_transcription(_tmp_video_path, _model_name, _language):
        raise RuntimeError("transcription exploded")

    monkeypatch.setattr(videos, "_transcribe_to_segments", fail_transcription)

    response = client.post(
        "/api/videos/upload",
        data={"slug": "beef-clip", "model": "small"},
        files={"file": ("beef-clip.mp4", b"fake-video-bytes", "video/mp4")},
    )

    assert response.status_code == 500
    assert response.json() == {"detail": "Upload/transcription failed: transcription exploded"}
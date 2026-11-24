import json
from pathlib import Path
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.api.endpoints import transcripts

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(transcripts.router)
    return TestClient(app)

@pytest.fixture()
def fake_data_dirs(tmp_path, monkeypatch):
    transcripts_dir = tmp_path / "transcripts"
    meta_dir = tmp_path / "meta"
    transcripts_dir.mkdir(parents=True, exist_ok=True)
    meta_dir.mkdir(parents=True, exist_ok=True)

    monkeypatch.setattr(transcripts, "TRANSCRIPTS_DIR", transcripts_dir)
    monkeypatch.setattr(transcripts, "META_DIR", meta_dir)

    return {
        "transcripts": transcripts_dir,
        "meta": meta_dir,
    }


def add_transcript(path: Path, content: str):
    path.write_text(content, encoding="utf-8")


def test_scanning_transcripts_returns_list_videos(client, fake_data_dirs):
    add_transcript(fake_data_dirs["transcripts"] / "ep1.txt", "episode 1")
    add_transcript(fake_data_dirs["transcripts"] / "ep2.vtt", "WEBVTT")
    add_transcript(fake_data_dirs["transcripts"] / "ep3.srt", "SRT FILE")
    add_transcript(fake_data_dirs["meta"] / "ep1.json", json.dumps({"title": "Episode One", "duration_seconds": 100}))

    response = client.get("/api/videos")

    assert response.status_code == 200
    format = response.json()
    assert format["count"] == 3

    items = {item["slug"]: item for item in format["items"]}
    assert items["ep1"]["title"] == "Episode One"
    assert items["ep1"]["duration_seconds"] == 100
    assert items["ep2"]["title"] == "ep2"
    assert items["ep2"]["slug"] == "ep2"
    assert items["ep2"]["duration_seconds"] is None
    assert items["ep3"]["slug"] == "ep3"
    assert items["ep3"]["duration_seconds"] is None



def test_get_transcript_returns_content(client, fake_data_dirs):
    add_transcript(fake_data_dirs["transcripts"] / "beef-nutrition.txt", "Beef nutrition content")

    response = client.get("/api/videos/beef-nutrition/transcript?format=txt")

    assert response.status_code == 200
    assert response.text == "Beef nutrition content"


def test_get_transcript_returns_404_when_missing(client, fake_data_dirs):
    response = client.get("/api/videos/nonexistence/transcript?format=txt")

    assert response.status_code == 404
    assert response.json()["detail"] == "transcript not found"


def test_upload_transcript_saves_file(client, fake_data_dirs):
    response = client.post(
        "/api/videos/file-123/transcript",
        data={"format": "txt"},
        files={"file": ("file-123.txt", b"new transcript", "text/plain")},
    )

    assert response.status_code == 200
    format = response.json()
    assert format["ok"] is True
    assert format["slug"] == "file-123"

    saved_file = fake_data_dirs["transcripts"] / "file-123.txt"
    assert saved_file.exists()
    assert saved_file.read_text(encoding="utf-8") == "new transcript"


def test_upload_transcript_invalid_format_returns_400(client, fake_data_dirs):
    response = client.post(
        "/api/videos/file-123/transcript",
        data={"format": "pdf"},
        files={"file": ("file-123.pdf", b"binary", "application/pdf")},
    )

    assert response.status_code == 400
    assert response.json()["detail"] == "format must be txt, srt, or vtt"


def test_debug_where_returns_current_data_dirs(client, fake_data_dirs):
    response = client.get("/api/debug/where")

    assert response.status_code == 200
    format = response.json()
    assert format["meta_dir"] == str(fake_data_dirs["meta"])
    assert format["transcripts_dir"] == str(fake_data_dirs["transcripts"])



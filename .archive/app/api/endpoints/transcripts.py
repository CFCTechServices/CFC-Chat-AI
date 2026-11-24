# app/api/endpoints/transcripts.py
from fastapi import APIRouter, HTTPException, Query
from fastapi.responses import JSONResponse, PlainTextResponse
from pathlib import Path
import os, json
from fastapi import UploadFile, File, Form

router = APIRouter(prefix="/api", tags=["transcripts"])

# Where transcript/meta files live. Overridable with DATA_ROOT env var.
REPO_DATA = Path(os.getenv("DATA_ROOT", Path(__file__).resolve().parents[3] / "data" / "videos"))
TRANSCRIPTS_DIR = REPO_DATA / "transcripts"
META_DIR = REPO_DATA / "meta"
TRANSCRIPTS_DIR.mkdir(parents=True, exist_ok=True)
META_DIR.mkdir(parents=True, exist_ok=True)

@router.get("/videos", response_class=JSONResponse)
def list_videos():
    """
    List videos by scanning the transcripts folder (txt/srt/vtt).
    If a matching meta JSON exists, merge title/duration.
    """
    items = []
    for p in sorted(TRANSCRIPTS_DIR.glob("*.*")):
        if p.suffix.lower() not in {".txt", ".srt", ".vtt"}:
            continue

        slug = p.stem
        title = slug
        duration_seconds = None

        meta_path = META_DIR / f"{slug}.json"
        if meta_path.exists():
            try:
                meta = json.loads(meta_path.read_text(encoding="utf-8"))
                title = meta.get("title", title)
                duration_seconds = meta.get("duration_seconds", duration_seconds)
            except Exception:
                # bad meta shouldn't hide the transcript
                pass

        items.append({
            "slug": slug,
            "title": title,
            "duration_seconds": duration_seconds
        })

    return {"count": len(items), "items": items}

@router.get("/videos/{slug}/transcript", response_class=PlainTextResponse)
def get_transcript(slug: str, format: str = Query("txt", pattern="^(txt|srt|vtt)$")):
    """Return a transcript for a video slug in txt|srt|vtt."""
    fp = TRANSCRIPTS_DIR / f"{slug}.{format.lower()}"
    if not fp.exists():
        raise HTTPException(status_code=404, detail="transcript not found")
    return fp.read_text(encoding="utf-8")

@router.post("/videos/{slug}/transcript", response_class=JSONResponse)
async def upload_transcript(
    slug: str,
    file: UploadFile = File(...),      # .txt | .srt | .vtt
    format: str = Form("txt"),         # optional form field, default txt
):
    fmt = format.lower().strip()
    if fmt not in {"txt", "srt", "vtt"}:
        raise HTTPException(status_code=400, detail="format must be txt, srt, or vtt")

    dest = TRANSCRIPTS_DIR / f"{slug}.{fmt}"
    content = await file.read()
    dest.write_bytes(content)

    return {"ok": True, "slug": slug, "saved": str(dest)}

@router.get("/debug/where")
def debug_where():
    return {
        "meta_dir": str(META_DIR),
        "transcripts_dir": str(TRANSCRIPTS_DIR),
        "meta_files": sorted([p.name for p in META_DIR.glob("*.json")]),
    }

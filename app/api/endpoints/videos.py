from __future__ import annotations
"""
Video ingestion flow: accepts uploads, runs Whisper
transcription, stores artifacts in Supabase, generates a markdown summary, and
indexes transcript chunks into Pinecone. Related helpers live in
app/services/supabase_content_repository.py and app/transcription/summarize_transcript.py.
"""
from fastapi import APIRouter, UploadFile, File, Form, HTTPException
from fastapi.responses import JSONResponse
from pathlib import Path
from typing import Dict, Any, List
from dotenv import load_dotenv
import tempfile
import whisper
import os
from datetime import timedelta
from supabase import create_client
from sentence_transformers import SentenceTransformer
from pinecone import Pinecone
import uuid

from app.config import settings


# Load .env from project root
BASE_DIR = Path(__file__).resolve().parents[2]
load_dotenv(BASE_DIR / ".env")

router = APIRouter(prefix="/api/videos", tags=["videos"])

# --------- small helpers (timestamp formatting + renderers) ---------
def _hhmmss(seconds: float) -> str:
    """Format seconds as an SRT-friendly timestamp."""
    td = timedelta(seconds=float(seconds))
    total = int(td.total_seconds())
    ms = int((float(seconds) - int(seconds)) * 1000)
    h = total // 3600
    m = (total % 3600) // 60
    s = total % 60
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"  # SRT uses comma

def _vtt_ts(seconds: float) -> str:
    """Format seconds for WebVTT output."""
    td = timedelta(seconds=float(seconds))
    total = int(td.total_seconds())
    ms = int((float(seconds) - int(seconds)) * 1000)
    h = total // 3600
    m = (total % 3600) // 60
    s = total % 60
    return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"  # VTT uses dot

def _render_txt(segments: List[Dict[str, Any]]) -> str:
    """Plain-text transcript with inline timestamps."""
    lines = [f"[{_vtt_ts(s['start'])} --> {_vtt_ts(s['end'])}]  {s['text'].strip()}" for s in segments]
    return "\n".join(lines) + "\n"

def _render_srt(segments: List[Dict[str, Any]]) -> str:
    """SubRip (.srt) transcript."""
    out = []
    for i, s in enumerate(segments, 1):
        out.append(str(i))
        out.append(f"{_hhmmss(s['start'])} --> {_hhmmss(s['end'])}")
        out.append(s["text"].strip())
        out.append("")  # blank line
    return "\n".join(out)

def _render_vtt(segments: List[Dict[str, Any]]) -> str:
    """WebVTT transcript."""
    out = ["WEBVTT", ""]
    for s in segments:
        out.append(f"{_vtt_ts(s['start'])} --> {_vtt_ts(s['end'])}")
        out.append(s["text"].strip())
        out.append("")
    return "\n".join(out)

def _simple_summary(segments: List[Dict[str, Any]]) -> str:
    """Low-effort markdown summary used until we wire in an LLM."""
    full = " ".join(s["text"].strip() for s in segments)
    words = full.split()
    if not words:
        return "# Topic Summary\n\n(No speech detected.)\n"
    n = max(1, len(words) // 4)
    bullets = [" ".join(words[i:i+n]) for i in range(0, min(4*n, len(words)), n)]
    lines = ["# Topic Summary", ""]
    for b in bullets:
        lines.append(f"- {b}")
    lines.append("")
    return "\n".join(lines)

# --------- Supabase helpers ---------
def build_local_repo():
    from app.services.supabase_content_repository import SupabaseContentRepository
    url = (os.getenv("SUPABASE_URL") or "").strip()
    # Backend video processing uses SERVICE_ROLE_KEY for storage operations
    key = (os.getenv("SUPABASE_SERVICE_ROLE_KEY") or "").strip()
    if not url or not key:
        raise RuntimeError(
            "Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY in .env. "
            "SERVICE_ROLE_KEY is required for video processing."
        )
    os.environ["SUPABASE_URL"] = url
    os.environ["SUPABASE_SERVICE_ROLE_KEY"] = key
    return SupabaseContentRepository()

def _bucket_name() -> str:
    """Return the bucket name used for all video-related assets."""
    bucket = getattr(settings, "SUPABASE_BUCKET_VIDEOS", None) or os.getenv("SUPABASE_BUCKET_VIDEOS") or os.getenv("SUPABASE_BUCKET") or "cfc-videos"
    return bucket.strip() if bucket else "cfc-videos"

def _upload_bytes(bucket: str, storage_path: str, data: bytes, content_type: str | None = None) -> str:
    """Upload raw bytes to Supabase Storage and return the public URL."""
    # Create Supabase client with SERVICE_ROLE_KEY for video operations
    url = os.getenv("SUPABASE_URL")
    key = os.getenv("SUPABASE_SERVICE_ROLE_KEY")
    if not url or not key:
        raise RuntimeError("SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY required for video operations")
    
    sb = create_client(url, key)
    opts = {"upsert": "true"}
    if content_type:
        opts["contentType"] = content_type
    sb.storage.from_(bucket).upload(storage_path, data, opts)
    return sb.storage.from_(bucket).get_public_url(storage_path)

# --------- Whisper transcription ---------
def _transcribe_to_segments(tmp_video_path: str, model_name: str = "small", language: str | None = None):
    model = whisper.load_model(model_name)
    result = model.transcribe(
        tmp_video_path,
        language=language,
        verbose=False,
        word_timestamps=False,
        condition_on_previous_text=True,
    )
    return [{"start": float(s["start"]), "end": float(s["end"]), "text": s["text"]} for s in result["segments"]]

# --------- API: upload video, transcribe, save outputs ---------
@router.post("/upload")
async def upload_and_transcribe(
    slug: str = Form(..., description="Video slug, e.g., cfc-vid-1"),
    file: UploadFile = File(..., description="Video/Audio file (.mp4, .mov, .m4a, etc.)"),
    model: str = Form("small", description="Whisper model: tiny/base/small/medium/large"),
    language: str | None = Form(None, description="Force language like 'en' (optional)"),
) -> JSONResponse:
    """
    1) Upload the original to Supabase: videos/original/{slug}.ext
    2) Transcribe with Whisper
    3) Save TXT/SRT/VTT + Markdown summary to Supabase
    4) Return public URLs
    """
    bucket = _bucket_name()

    # sanitize slug
    slug = slug.strip().lower().replace(" ", "-")
    if not slug or "/" in slug:
        raise HTTPException(400, "slug must be non-empty and cannot contain '/'")

    tmp_path = None
    try:
        # 0) read file bytes (and validate)
        raw = await file.read()
        if not raw:
            raise HTTPException(400, "empty file")
        ext = (Path(file.filename).suffix or ".mp4").lower()

        # stash to a temp file so Whisper can read it
        with tempfile.NamedTemporaryFile(delete=False, suffix=ext) as tmp:
            tmp.write(raw)
            tmp_path = tmp.name

        # 1) upload original (single-level path)
        video_path = f"videos/original/{slug}{ext}"
        video_url = _upload_bytes(bucket, video_path, raw, content_type="video/mp4")

        # 2) whisper
        segments = _transcribe_to_segments(tmp_path, model_name=model, language=language)

        # 3) render formats
        txt_string = _render_txt(segments)
        srt_string = _render_srt(segments)
        vtt_string = _render_vtt(segments)
        summary_md = _simple_summary(segments)

        # 4) upload outputs (with content types)
        txt_url = _upload_bytes(bucket, f"videos/transcript/{slug}.txt", txt_string.encode("utf-8"), "text/plain")
        srt_url = _upload_bytes(bucket, f"videos/transcript/{slug}.srt", srt_string.encode("utf-8"), "application/x-subrip")
        vtt_url = _upload_bytes(bucket, f"videos/transcript/{slug}.vtt", vtt_string.encode("utf-8"), "text/vtt")
        sum_url = _upload_bytes(bucket, f"videos/summaries/{slug}.md", summary_md.encode("utf-8"), "text/markdown")

        chunk_count = _index_transcript_chunks(
            slug=slug,
            segments=segments,
            original_video_url=video_url,
            txt_url=txt_url,
            srt_url=srt_url,
            vtt_url=vtt_url,
        )


        return JSONResponse(
            {
                "ok": True,
                "slug": slug,
                "original_video_url": video_url,
                "transcripts": {"txt": txt_url, "srt": srt_url, "vtt": vtt_url},
                "summary_md": sum_url,
            }
        )

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Upload/transcription failed: {e}")
    finally:
        if tmp_path and os.path.exists(tmp_path):
            try:
                os.remove(tmp_path)
            except Exception:
                pass

def _build_chunks_from_segments(
    slug: str,
    segments: List[Dict[str, Any]],
    max_chars: int = 800,
    overlap_chars: int = 120
) -> List[Dict[str, Any]]:
    """
    Greedy chunker that concatenates adjacent segments until ~max_chars,
    keeps the earliest start and latest end for the chunk.
    """
    chunks: List[Dict[str, Any]] = []
    buf = []
    cur_start = None
    cur_end = None
    cur_len = 0

    def flush():
        nonlocal buf, cur_start, cur_end, cur_len
        if not buf:
            return
        text = " ".join(buf).strip()
        chunks.append({
            "slug": slug,
            "text": text,
            "start": cur_start,
            "end": cur_end,
        })
        buf = []
        cur_start = None
        cur_end = None
        cur_len = 0

    for s in segments:
        t = s["text"].strip()
        if not t:
            continue
        if cur_start is None:
            cur_start = s["start"]
        cur_end = s["end"]

        if cur_len + len(t) + 1 > max_chars:
            # allow some overlap to help retrieval continuity
            overflow = t[:overlap_chars]
            flush()
            # seed next chunk with a tiny overlap from previous text
            if overflow:
                buf = [overflow]
                cur_start = s["start"]
                cur_end = s["end"]
                cur_len = len(overflow)
        else:
            buf.append(t)
            cur_len += len(t) + 1
    flush()
    return chunks

# ---------- Embeddings & Pinecone ----------

def _embedder():
    """Load the SentenceTransformer model used for chunk embeddings."""
    model_name = os.getenv("EMBED_MODEL", "all-MiniLM-L6-v2")
    return SentenceTransformer(model_name)

def _pinecone():
    """Bootstrap the Pinecone client."""
    api_key = os.getenv("PINECONE_API_KEY")
    if not api_key:
        raise RuntimeError("Missing PINECONE_API_KEY in .env")
    return Pinecone(api_key=api_key)

def _pinecone_index():
    """Return the configured Pinecone index handle."""
    pc = _pinecone()
    index_name = getattr(settings, "PINECONE_VIDEO_INDEX_NAME", settings.PINECONE_INDEX_NAME)
    return pc.Index(index_name)

def _pinecone_namespace():
    """Return Pinecone namespace from settings or env; None for default namespace."""
    ns = getattr(settings, "PINECONE_NAMESPACE", None)
    if ns:
        return ns
    env_ns = (os.getenv("PINECONE_NAMESPACE") or "").strip()
    return env_ns or None

def _index_transcript_chunks(
    slug: str,
    segments: List[Dict[str, Any]],
    original_video_url: str,
    txt_url: str,
    srt_url: str,
    vtt_url: str,
) -> int:
    """Chunk transcript, embed, and upsert to Pinecone. Returns # vectors upserted."""
    chunks = _build_chunks_from_segments(slug, segments)
    if not chunks:
        return 0

    model = _embedder()
    texts = [c["text"] for c in chunks]
    vectors = model.encode(texts, normalize_embeddings=True).tolist()

    index = _pinecone_index()
    namespace = _pinecone_namespace()

    items = []
    for c, vec in zip(chunks, vectors):
        items.append({
            "id": str(uuid.uuid4()),
            "values": vec,
            "metadata": {
                "source": slug,
                "source_type": "video",
                "slug": slug,
                "text": c["text"],
                "start_seconds": float(c["start"]) if c.get("start") is not None else None,
                "end_seconds": float(c["end"]) if c.get("end") is not None else None,
                "video_url": original_video_url,
                "txt_url": txt_url,
                "srt_url": srt_url,
                "vtt_url": vtt_url,
            }
        })

    # upsert in batches (sane default)
    B = 100
    for i in range(0, len(items), B):
        batch = items[i:i+B]
        if namespace:
            index.upsert(vectors=batch, namespace=namespace)
        else:
            index.upsert(vectors=batch)

    return len(items)



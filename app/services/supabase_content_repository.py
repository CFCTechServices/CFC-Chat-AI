# app/services/supabase_content_repository.py
from __future__ import annotations
import os, json
from dataclasses import dataclass
from typing import Dict, List
from pathlib import Path
from supabase import create_client
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parents[2]
load_dotenv(BASE_DIR / ".env")

@dataclass
class StoredSection:
    section_id: str
    storage_path: str
    public_url: str

@dataclass
class StoredImage:
    image_id: str
    storage_path: str
    public_url: str

class SupabaseContentRepository:
    """Handles persistence in Supabase Storage."""
    def __init__(self) -> None:
        self.url = os.getenv("SUPABASE_URL")
        # Backend content repository uses SERVICE_ROLE_KEY for storage operations
        self.key = os.getenv("SUPABASE_SERVICE_ROLE_KEY")
        # Separate buckets for documents and videos
        self.doc_bucket = os.getenv("SUPABASE_BUCKET_DOCS") or os.getenv("SUPABASE_BUCKET", "cfc-docs")
        self.video_bucket = os.getenv("SUPABASE_BUCKET_VIDEOS") or os.getenv("SUPABASE_BUCKET", "cfc-videos")
        self.bucket = self.doc_bucket  # backward-compat alias

        if not self.url or not self.key:
            raise RuntimeError(
                "Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY. "
                "SERVICE_ROLE_KEY is required for backend storage operations."
            )

        self.client = create_client(self.url, self.key)

    # ----- generic helpers -----
    def public_url(self, storage_path: str) -> str:
        return self.client.storage.from_(self.bucket).get_public_url(storage_path)

    def upload_bytes(self, storage_path: str, data: bytes, content_type: str | None = None) -> str:
        # Important: use correct option keys for storage API
        opts = {"upsert": "true"}
        if content_type:
            opts["contentType"] = content_type
        self.client.storage.from_(self.bucket).upload(storage_path, data, opts)
        return self.public_url(storage_path)

    # ----- original video -----
    def upload_video_original(self, slug: str, file_bytes: bytes, filename: str) -> str:
        ext = Path(filename).suffix or ".mp4"
        storage_path = f"videos/original/{slug}/{Path(filename).name}"
        return self.upload_bytes(storage_path, file_bytes, content_type="video/mp4")

    # ----- transcripts -----
    def save_transcript(self, slug: str, fmt: str, data: bytes | str) -> str:
        fmt = fmt.lower()
        assert fmt in {"txt", "srt", "vtt"}, "format must be txt|srt|vtt"
        storage_path = f"videos/transcript/{slug}.{fmt}"
        if isinstance(data, str):
            data = data.encode("utf-8")
        content_type = {"txt": "text/plain", "srt": "application/x-subrip", "vtt": "text/vtt"}[fmt]
        return self.upload_bytes(storage_path, data, content_type)

    # ----- summaries -----
    def save_summary(self, slug: str, data: bytes | str, ext: str = "md") -> str:
        ext = ext.lstrip(".").lower()
        storage_path = f"videos/summaries/{slug}.{ext}"
        if isinstance(data, str):
            data = data.encode("utf-8")
        content_type = "text/markdown" if ext == "md" else "text/plain"
        return self.upload_bytes(storage_path, data, content_type)

    # (optional) legacy doc/image methods if you still need them:
    def store_section(self, doc_id: str, section: Dict) -> StoredSection:
        section_id = section["section_id"]
        storage_path = f"docs/{doc_id}/sections/{section_id}.json"
        data = json.dumps(section, ensure_ascii=False, indent=2).encode("utf-8")
        self.client.storage.from_(self.bucket).upload(storage_path, data, {"upsert": "true"})
        return StoredSection(section_id, storage_path, self.public_url(storage_path))

    def store_image(self, doc_id: str, image: Dict) -> StoredImage:
        image_id = image["image_id"]
        filename = image.get("suggested_name") or image.get("filename") or f"{image_id}.png"
        storage_path = f"docs/{doc_id}/images/{filename}"
        data: bytes = image["data"]
        self.client.storage.from_(self.bucket).upload(storage_path, data, {"upsert": "true"})
        return StoredImage(image_id, storage_path, self.public_url(storage_path))

    def store_images(self, doc_id: str, images: List[Dict]) -> Dict[str, StoredImage]:
        return {img["image_id"]: self.store_image(doc_id, img) for img in images}

    def create_signed_url(self, storage_path: str, source_type: str = "document", expires_in: int = 300) -> str:
        """Return a signed URL for a file; picks the correct bucket by source_type."""
        bucket = self.video_bucket if source_type == "video" else self.doc_bucket
        result = self.client.storage.from_(bucket).create_signed_url(storage_path, expires_in)
        url = result.get("signedURL") or result.get("signedUrl") or result.get("signed_url") or ""
        if not url:
            raise ValueError("No signed URL returned")
        return url

    def list_storage(self, prefix: str, source_type: str = "document") -> list:
        """List files at a storage prefix; picks the correct bucket by source_type."""
        bucket = self.video_bucket if source_type == "video" else self.doc_bucket
        return self.client.storage.from_(bucket).list(prefix) or []

    def delete_document_content(self, doc_id: str) -> None:
        """Delete all storage files and DB rows associated with a doc_id."""
        for subfolder in ["sections", "images", "original"]:
            try:
                prefix = f"docs/{doc_id}/{subfolder}"
                items = self.client.storage.from_(self.doc_bucket).list(prefix)
                if items:
                    paths = [f"{prefix}/{item['name']}" for item in items if item.get("name")]
                    if paths:
                        self.client.storage.from_(self.doc_bucket).remove(paths)
            except Exception:
                pass

        try:
            self.client.table("document_chunks").delete().eq("doc_id", doc_id).execute()
        except Exception:
            pass

    def delete_video_content(self, slug: str) -> None:
        """Delete all storage files and DB rows associated with a video slug."""
        for subfolder in ["original", "transcripts", "summary"]:
            try:
                prefix = f"videos/{slug}/{subfolder}"
                items = self.client.storage.from_(self.video_bucket).list(prefix)
                if items:
                    paths = [f"{prefix}/{item['name']}" for item in items if item.get("name")]
                    if paths:
                        self.client.storage.from_(self.video_bucket).remove(paths)
            except Exception:
                pass

        try:
            self.client.table("document_chunks").delete().eq("doc_id", slug).execute()
        except Exception:
            pass

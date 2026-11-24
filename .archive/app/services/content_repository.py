"""Local content repository simulating Supabase storage."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional

from app.config import settings


@dataclass
class StoredSection:
    """Metadata returned after storing a section."""

    section_id: str
    storage_path: str


@dataclass
class StoredImage:
    """Metadata returned after storing an image."""

    image_id: str
    storage_path: str


def _finalize_filename(requested: Optional[str], fallback: str, required_suffix: Optional[str] = None) -> str:
    """Return a sanitized filename honoring the requested name when possible."""
    candidate = (requested or "").strip()
    if candidate:
        candidate = Path(candidate).name  # strip any path components
    if not candidate:
        candidate = fallback

    if required_suffix:
        suffix = required_suffix if required_suffix.startswith(".") else f".{required_suffix}"
        if not candidate.lower().endswith(suffix.lower()):
            candidate = f"{candidate}{suffix}"
    return candidate


class ContentRepository:
    """Handles persistence of section JSON and images.

    This implementation writes to the local filesystem to mirror how
    Supabase Storage could be used. Replacing this with Supabase SDK
    calls later requires changing only this class.
    """

    def __init__(self, root: Optional[Path] = None) -> None:
        self.root = root or settings.LOCAL_CONTENT_ROOT
        self.root.mkdir(parents=True, exist_ok=True)

    def _doc_root(self, doc_id: str) -> Path:
        return self.root / doc_id

    def store_section(self, doc_id: str, section: Dict) -> StoredSection:
        doc_root = self._doc_root(doc_id)
        target_dir = doc_root / "sections"
        target_dir.mkdir(parents=True, exist_ok=True)

        section_id = section["section_id"]
        suggested = section.get("suggested_name")
        filename = _finalize_filename(suggested, f"{section_id}.json", required_suffix=".json")

        target_path = target_dir / filename
        with target_path.open("w", encoding="utf-8") as handle:
            json.dump(section, handle, ensure_ascii=False, indent=2)

        storage_path = f"docs/{doc_id}/sections/{filename}"
        return StoredSection(section_id=section_id, storage_path=storage_path)

    def store_image(self, doc_id: str, image: Dict) -> StoredImage:
        doc_root = self._doc_root(doc_id)
        target_dir = doc_root / "images"
        target_dir.mkdir(parents=True, exist_ok=True)

        image_id = image["image_id"]
        suggested = image.get("suggested_name") or image.get("filename")
        extension = image.get("extension") or ""
        fallback = f"{image_id}{extension}"
        filename = _finalize_filename(suggested, fallback, required_suffix=(extension if extension else None))

        target_path = target_dir / filename
        data: bytes = image["data"]
        with target_path.open("wb") as handle:
            handle.write(data)

        storage_path = f"docs/{doc_id}/images/{filename}"
        return StoredImage(image_id=image_id, storage_path=storage_path)

    def store_images(self, doc_id: str, images: List[Dict]) -> Dict[str, StoredImage]:
        stored: Dict[str, StoredImage] = {}
        for image in images:
            stored_image = self.store_image(doc_id, image)
            stored[stored_image.image_id] = stored_image
        return stored


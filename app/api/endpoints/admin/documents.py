from __future__ import annotations

import logging
from pathlib import Path
from typing import Any, Dict, List

from fastapi import APIRouter, Depends, File, HTTPException, UploadFile
from pydantic import BaseModel

from app.core.auth import get_current_admin
from app.core.supabase_service import supabase
from app.core.vector_store import VectorStore
from app.services.supabase_content_repository import SupabaseContentRepository
from app.config import settings
from app.api.models.requests import IngestRequest

logger = logging.getLogger(__name__)
router = APIRouter()

_vector_store = VectorStore()
_content_repository = SupabaseContentRepository()


class DocumentSummary(BaseModel):
    doc_id: str
    source: str
    source_type: str
    chunk_count: int


class ListDocumentsResponse(BaseModel):
    documents: List[DocumentSummary]
    total: int


# ---------------------------------------------------------------------------
# GET /api/admin/documents  — list all ingested documents
# ---------------------------------------------------------------------------

@router.get("/documents", response_model=ListDocumentsResponse)
async def list_documents(admin: dict = Depends(get_current_admin)):
    try:
        resp = supabase.table("document_chunks").select("doc_id, source, source_type").execute()
        rows = getattr(resp, "data", []) or []
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to fetch documents: {exc}")

    # Aggregate in Python: count chunks per doc_id
    seen: Dict[str, DocumentSummary] = {}
    for row in rows:
        doc_id = row.get("doc_id") or ""
        if not doc_id:
            continue
        if doc_id not in seen:
            seen[doc_id] = DocumentSummary(
                doc_id=doc_id,
                source=row.get("source") or "",
                source_type=row.get("source_type") or "document",
                chunk_count=1,
            )
        else:
            seen[doc_id].chunk_count += 1

    documents = sorted(seen.values(), key=lambda d: d.source)
    return ListDocumentsResponse(documents=documents, total=len(documents))


# ---------------------------------------------------------------------------
# GET /api/admin/documents/{doc_id}/download  — signed URL for original file
# ---------------------------------------------------------------------------

@router.get("/documents/{doc_id}/download")
async def download_document(doc_id: str, admin: dict = Depends(get_current_admin)):
    # Look up source and source_type from Supabase
    try:
        resp = (
            supabase.table("document_chunks")
            .select("source, source_type")
            .eq("doc_id", doc_id)
            .limit(1)
            .execute()
        )
        rows = getattr(resp, "data", []) or []
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to look up document: {exc}")

    if not rows:
        raise HTTPException(status_code=404, detail=f"Document '{doc_id}' not found.")

    source_type = rows[0].get("source_type", "document")
    raw_source = rows[0].get("source", "")

    if source_type == "video":
        # List files in videos/{slug}/original/ to find the actual filename+ext
        prefix = f"videos/{doc_id}/original"
        try:
            items = _content_repository.list_storage(prefix, source_type="video")
            if not items:
                raise ValueError("No original video found")
            filename = items[0]["name"]
            storage_path = f"{prefix}/{filename}"
        except Exception:
            raise HTTPException(status_code=404, detail="Original video not found in storage. Re-upload to enable downloads.")
    else:
        filename = Path(raw_source).name if raw_source else ""
        if not filename:
            raise HTTPException(status_code=404, detail="Could not determine filename for this document.")
        storage_path = f"docs/{doc_id}/original/{filename}"

    try:
        signed_url = _content_repository.create_signed_url(storage_path, source_type=source_type, expires_in=300)
    except Exception:
        raise HTTPException(status_code=404, detail="Original file not found in storage. Re-upload to enable downloads.")

    return {"url": signed_url, "filename": filename}


# ---------------------------------------------------------------------------
# DELETE /api/admin/documents/{doc_id}  — remove from all stores
# ---------------------------------------------------------------------------

@router.delete("/documents/{doc_id}")
async def delete_document(doc_id: str, admin: dict = Depends(get_current_admin)):
    await _purge_document(doc_id)
    return {"success": True, "message": f"Document '{doc_id}' deleted from all stores."}


# ---------------------------------------------------------------------------
# PUT /api/admin/documents/{doc_id}/replace  — swap content for a doc
# ---------------------------------------------------------------------------

@router.put("/documents/{doc_id}/replace")
async def replace_document(
    doc_id: str,
    file: UploadFile = File(...),
    admin: dict = Depends(get_current_admin),
):
    # Fetch the original source path so we can overwrite the same file on disk
    try:
        resp = (
            supabase.table("document_chunks")
            .select("source")
            .eq("doc_id", doc_id)
            .limit(1)
            .execute()
        )
        rows = getattr(resp, "data", []) or []
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to look up document: {exc}")

    if not rows:
        raise HTTPException(status_code=404, detail=f"Document '{doc_id}' not found in knowledge base.")

    original_source = rows[0].get("source", "")
    original_path = Path(original_source) if original_source else None

    # Determine where to save the replacement file
    if original_path and original_path.parent.exists():
        save_path = original_path.parent / file.filename
    else:
        settings.DOCUMENTS_DIR.mkdir(parents=True, exist_ok=True)
        save_path = settings.DOCUMENTS_DIR / file.filename

    contents = await file.read()
    try:
        with save_path.open("wb") as f:
            f.write(contents)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to save replacement file: {exc}")

    # Purge old data from all stores
    await _purge_document(doc_id)

    # Re-ingest using the existing endpoint logic
    from app.api.endpoints.ingest import ingest_document
    ingest_req = IngestRequest(filename=file.filename)
    try:
        result = await ingest_document(ingest_req)
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Ingestion failed after replace: {exc}")

    to_dict = getattr(result, "model_dump", None) or getattr(result, "dict")
    return {
        "success": True,
        "message": f"Document '{doc_id}' replaced successfully.",
        "ingestion": to_dict(),
    }


# ---------------------------------------------------------------------------
# Shared purge helper
# ---------------------------------------------------------------------------

async def _purge_document(doc_id: str) -> None:
    """Remove a document from Pinecone, Supabase DB, and Supabase Storage."""
    # 1) Fetch chunk IDs and source_type before deleting from DB
    chunk_ids: List[str] = []
    source_type = "document"
    try:
        resp = (
            supabase.table("document_chunks")
            .select("chunk_id, source_type")
            .eq("doc_id", doc_id)
            .execute()
        )
        rows = getattr(resp, "data", []) or []
        chunk_ids = [r["chunk_id"] for r in rows if r.get("chunk_id")]
        if rows:
            source_type = rows[0].get("source_type") or "document"
    except Exception as exc:
        logger.warning("Could not fetch chunk IDs for %s: %s", doc_id, exc)

    # 2) Delete vectors from Pinecone
    if chunk_ids:
        try:
            _vector_store.delete_document(chunk_ids)
        except Exception as exc:
            logger.warning("Pinecone delete failed for %s: %s", doc_id, exc)

    # 3) Delete from Supabase Storage + DB
    try:
        if source_type == "video":
            _content_repository.delete_video_content(doc_id)
        else:
            _content_repository.delete_document_content(doc_id)
    except Exception as exc:
        logger.warning("Supabase content delete failed for %s: %s", doc_id, exc)

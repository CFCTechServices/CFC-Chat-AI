from __future__ import annotations
import logging
from pathlib import Path
from typing import Any, Dict, List, Tuple

from fastapi import APIRouter, HTTPException

from app.api.models.requests import BulkIngestRequest, IngestRequest
from app.api.models.responses import BulkIngestResponse, IngestResponse
from app.config import settings
from app.core.embeddings import EmbeddingModel
from app.core.vector_store import VectorStore
from app.services.document_processor import DocumentProcessor
from app.services.content_repository import ContentRepository
from app.services.supabase_content_repository import SupabaseContentRepository
from app.core.supabase_service import supabase



logger = logging.getLogger(__name__)
router = APIRouter(prefix="/ingest", tags=["ingestion"])

# Initialize services
_document_processor = DocumentProcessor()
_vector_store = VectorStore()
_embedding_model = EmbeddingModel()
_content_repository = ContentRepository()

if settings.SUPABASE_URL and settings.SUPABASE_BUCKET:
    _content_repository = SupabaseContentRepository()
    logger.info("Using SupabaseContentRepository for document storage.")
else:
    _content_repository = ContentRepository()
    logger.info("Using local ContentRepository for document storage.")


@router.post("/document", response_model=IngestResponse, include_in_schema=False)
async def ingest_document(request: IngestRequest) -> IngestResponse:
    """Ingest a single document into storage and the vector index."""
    try:
        file_path = _locate_document(request.filename)
        processed = _document_processor.process_document(file_path)

        if not processed.get("success"):
            raise HTTPException(status_code=400, detail=processed.get("error", "Unknown processing error"))

        updated = _persist_document_content(processed)
        vectors, chunk_count = _prepare_vectors(updated)

        if vectors:
            _vector_store.upsert_vectors(vectors)

        logger.info(
            "Successfully ingested %s: %s sections, %s chunks, %s images",
            request.filename,
            updated["section_count"],
            chunk_count,
            updated["image_count"],
        )

        return IngestResponse(
            success=True,
            message=f"Successfully ingested {request.filename}",
            chunks_processed=chunk_count,
            sections_processed=updated["section_count"],
            images_processed=updated["image_count"],
            doc_id=updated["doc_id"],
            source=str(file_path),
        )

    except HTTPException:
        raise
    except Exception as exc:
        logger.error("Error ingesting document %s: %s", request.filename, exc)
        raise HTTPException(status_code=500, detail=str(exc))


@router.post("/bulk", response_model=BulkIngestResponse, include_in_schema=False)
async def bulk_ingest(request: BulkIngestRequest) -> BulkIngestResponse:
    """Ingest all documents from the documents directory."""
    try:
        directory = settings.DOCUMENTS_DIR / request.subdirectory if request.subdirectory else settings.DOCUMENTS_DIR
        if not directory.exists():
            raise HTTPException(status_code=404, detail=f"Directory not found: {directory}")

        results = _document_processor.process_directory(directory)
        if not results:
            return BulkIngestResponse(
                success=True,
                message="No supported documents found in directory",
                successful_files=0,
                failed_files=0,
                total_chunks=0,
            )

        total_chunks = 0
        total_sections = 0
        total_images = 0
        successful_files = 0
        failed_files = 0
        errors: List[Dict[str, str]] = []

        for result in results:
            if result.get("success"):
                updated = _persist_document_content(result)
                vectors, chunk_count = _prepare_vectors(updated)
                if vectors:
                    _vector_store.upsert_vectors(vectors)
                total_chunks += chunk_count
                total_sections += updated["section_count"]
                total_images += updated["image_count"]
                successful_files += 1
                logger.info(
                    "Processed %s: %s sections, %s chunks", result.get("source"), updated["section_count"], chunk_count
                )
            else:
                failed_files += 1
                errors.append({
                    "file": Path(result.get("source", "unknown")).name,
                    "error": result.get("error", "Unknown error"),
                })
                logger.error("Failed to process %s: %s", result.get("source"), result.get("error"))

        return BulkIngestResponse(
            success=True,
            message=f"Bulk ingestion completed: {successful_files} successful, {failed_files} failed",
            successful_files=successful_files,
            failed_files=failed_files,
            total_chunks=total_chunks,
            total_sections=total_sections,
            total_images=total_images,
            errors=errors or None,
        )

    except HTTPException:
        raise
    except Exception as exc:
        logger.error("Error in bulk ingestion: %s", exc)
        raise HTTPException(status_code=500, detail=str(exc))


def _locate_document(filename: str) -> Path:
    possible_paths = [
        settings.DOCUMENTS_DIR / filename,
        settings.DOCUMENTS_DIR / "docx" / filename,
        settings.DOCUMENTS_DIR / "doc" / filename,
        settings.DATA_DIR / filename,
    ]
    for path in possible_paths:
        if path.exists():
            return path
    raise HTTPException(status_code=404, detail=f"File '{filename}' not found in any of the expected locations")


def _persist_document_content(processed: Dict[str, Any]) -> Dict[str, Any]:
    doc_id: str = processed["doc_id"]
    sections: List[Dict] = processed.get("sections", [])
    images: List[Dict] = processed.get("images", [])

    stored_images = _content_repository.store_images(doc_id, images) if images else {}
    placeholder_to_path: Dict[str, str] = {}
    for image in images:
        storage = stored_images.get(image["image_id"])
        if storage:
            placeholder = f"images/{image['image_id']}{image.get('extension', '')}"
            placeholder_to_path[placeholder] = storage.storage_path
            image["storage_path"] = storage.storage_path
            image.pop("data", None)

    section_paths: Dict[str, str] = {}
    for section in sections:
        for block in section.get("blocks", []):
            if block.get("type") == "image":
                placeholder = block.get("path")
                if placeholder and placeholder in placeholder_to_path:
                    block["path"] = placeholder_to_path[placeholder]
                    block["storage_path"] = placeholder_to_path[placeholder]
        section["storage_path"] = f"docs/{doc_id}/sections/{section['section_id']}.json"
        stored_section = _content_repository.store_section(doc_id, section)
        section_paths[section["section_id"]] = stored_section.storage_path
        section["storage_path"] = stored_section.storage_path

    updated_chunks: List[Dict] = []
    for chunk in processed.get("chunks", []):
        chunk_copy = {**chunk}
        chunk_copy["section_path"] = section_paths.get(chunk_copy["section_id"])
        chunk_copy["image_paths"] = [placeholder_to_path.get(path, path) for path in chunk_copy.get("image_paths", [])]
        updated_chunks.append(chunk_copy)

    processed["sections"] = sections
    processed["images"] = images
    processed["chunks"] = updated_chunks
    processed["section_count"] = len(sections)
    processed["image_count"] = len(images)
    processed["section_paths"] = section_paths
    processed["chunk_count"] = len(updated_chunks)
    return processed


def _prepare_vectors(processed: Dict[str, Any]) -> Tuple[List[Tuple[str, List[float], Dict]], int]:
    """Encode chunk texts, persist them to Supabase `document_chunks` table,
    and return Pinecone-ready vectors with minimal metadata.
    """
    chunks: List[Dict] = processed.get("chunks", [])
    if not chunks:
        return [], 0

    doc_id = processed.get("doc_id")
    source = processed.get("source")

    # 1) Encode embeddings
    texts = [chunk.get("text", "") for chunk in chunks]
    embeddings = _embedding_model.encode(texts)

    # 2) Persist chunk rows to Supabase (upsert to avoid duplicates)
    # Map section_id -> section_title when available
    section_title_map = {}
    for s in processed.get("sections", []):
        sid = s.get("section_id")
        if sid:
            section_title_map[sid] = s.get("title") or s.get("section_title") or s.get("suggested_name")

    rows = []
    for chunk in chunks:
        rows.append({
            "chunk_id": chunk.get("chunk_id"),
            "doc_id": doc_id,
            "section_id": chunk.get("section_id"),
            "section_title": section_title_map.get(chunk.get("section_id")),
            "content": chunk.get("text"),
            "image_paths": chunk.get("image_paths", []),
            "source": source,
            "source_type": chunk.get("source_type", "document"),
            "start_seconds": chunk.get("start_seconds"),
            "end_seconds": chunk.get("end_seconds"),
            "video_url": chunk.get("video_url"),
            "txt_url": chunk.get("txt_url"),
            "srt_url": chunk.get("srt_url"),
            "vtt_url": chunk.get("vtt_url"),
        })

    try:
        # use upsert so re-ingestion won't error on duplicate PKs
        supabase.table("document_chunks").upsert(rows).execute()
    except Exception:
        # best-effort: log is handled by caller; don't fail ingestion entirely here
        pass

    # 3) Build Pinecone vectors with minimal metadata (no full text)
    vectors: List[Tuple[str, List[float], Dict]] = []
    for index, chunk in enumerate(chunks):
        metadata = {
            "doc_id": doc_id,
            "section_id": chunk.get("section_id"),
            "source": source,
            "source_type": chunk.get("source_type", "document"),
        }
        vectors.append((chunk.get("chunk_id"), embeddings[index], metadata))

    return vectors, len(chunks)









from fastapi import APIRouter, Depends
import logging
from pathlib import Path
from app.core.auth import get_current_admin
from app.config import settings
from .models import DocumentInfo, IngestionStatsResponse

logger = logging.getLogger(__name__)
router = APIRouter()

SUPPORTED_EXTENSIONS = {".pdf", ".doc", ".docx", ".txt", ".md"}


@router.get("/ingestion/stats", response_model=IngestionStatsResponse)
async def get_ingestion_stats(admin: dict = Depends(get_current_admin)):
    docs_dir = settings.DOCUMENTS_DIR
    processed_dir = settings.PROCESSED_DIR / "content_repository"

    # Collect all document files from documents dir (including doc/ and docx/ subdirs)
    doc_files: list[tuple[str, int]] = []
    if docs_dir.exists():
        for path in docs_dir.rglob("*"):
            if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS:
                doc_files.append((path.name, path.stat().st_size))

    # Determine which docs have been processed
    processed_names: set[str] = set()
    if processed_dir.exists():
        for item in processed_dir.iterdir():
            if item.is_dir():
                processed_names.add(item.name)

    documents = []
    for name, size in doc_files:
        stem = Path(name).stem
        status = "ingested" if stem in processed_names else "pending"
        documents.append(DocumentInfo(name=name, size=size, status=status))

    completed = sum(1 for d in documents if d.status == "ingested")

    return IngestionStatsResponse(
        total_documents=len(documents),
        processing=0,
        completed=completed,
        documents=documents,
    )

from fastapi import APIRouter, UploadFile, File, HTTPException
from typing import List, Dict, Any
from supabase import create_client
import os
import uuid
from pathlib import Path

router = APIRouter()

# Supabase storage settings
SUPABASE_URL = os.getenv("SUPABASE_URL")

# Backend file upload uses SERVICE_ROLE_KEY for storage operations
# This ensures proper permissions for backend file management
SUPABASE_SERVICE_ROLE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY")

SUPABASE_BUCKET = os.getenv("SUPABASE_BUCKET", "cfc-docs")

# Local storage fallback
UPLOAD_DIR = Path(__file__).parent.parent.parent / "data" / "uploads"
UPLOAD_DIR.mkdir(parents=True, exist_ok=True)


@router.post("/upload")
async def upload_file(file: UploadFile = File(...)):
    """Upload a file and immediately trigger ingestion.

    Behavior:
    - Always saves the uploaded file to local documents directory for ingestion.
    - If Supabase settings are present, also uploads to Supabase Storage (best-effort).
    - Calls the existing ingestion routine and returns its result.
    """

    if not file or not file.filename:
        raise HTTPException(status_code=400, detail="No file provided")

    # Read file bytes once
    contents = await file.read()

    # Save locally so /ingest logic can find it
    documents_dir = settings.DOCUMENTS_DIR
    documents_dir.mkdir(parents=True, exist_ok=True)
    local_path = documents_dir / file.filename
    try:
        with local_path.open("wb") as f:
            f.write(contents)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Failed to save file locally: {exc}")

    # Optionally upload to Supabase (do not fail the request if this part errors)
    supabase_info = None
    if SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY and SUPABASE_BUCKET:
        try:
            sb = create_client(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY)
            res = sb.storage.from_(SUPABASE_BUCKET).upload(file.filename, contents)
            sup_path = getattr(res, "path", None) if res is not None else None
            supabase_info = {"path": sup_path}
        except Exception as sb_exc:
            supabase_info = {"error": str(sb_exc)}

    # Trigger ingestion using the existing endpoint logic
    ingestion_data = None
    try:
        ingest_req = IngestRequest(filename=file.filename)
        ingest_result = await ingest_document(ingest_req)
        # Pydantic v1/v2 compatibility
        to_dict = getattr(ingest_result, "model_dump", None) or getattr(ingest_result, "dict")
        ingestion_data = to_dict()
    except Exception as ing_exc:
        ingestion_data = {"success": False, "error": str(ing_exc)}

    return {
        "message": "File uploaded and ingestion triggered",
        "local_path": str(local_path),
        "supabase": supabase_info,
        "ingestion": ingestion_data,
    }


@router.post("/bulk")
async def bulk_upload(files: List[UploadFile] = File(...)) -> Dict[str, Any]:
    """Upload multiple files and trigger ingestion for each.

    Saves each file to the local documents directory, optionally mirrors to Supabase,
    and calls the ingestion routine per file. Returns per-file results plus summary.
    """
    if not files:
        raise HTTPException(status_code=400, detail="No files provided")

    documents_dir = settings.DOCUMENTS_DIR
    documents_dir.mkdir(parents=True, exist_ok=True)

    results: List[Dict[str, Any]] = []
    success_count = 0
    failure_count = 0

    sb = None
    if SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY and SUPABASE_BUCKET: # Changed SUPABASE_KEY to SUPABASE_SERVICE_ROLE_KEY
        try:
            sb = create_client(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY) # Changed SUPABASE_KEY to SUPABASE_SERVICE_ROLE_KEY
        except Exception:
            sb = None

    for file in files:
        item: Dict[str, Any] = {"filename": getattr(file, "filename", None)}
        try:
            if not file or not file.filename:
                raise ValueError("Missing filename")

            # Generate a unique file ID and name for Supabase
            file_id = str(uuid.uuid4())
            original_filename = file.filename
            file_name_in_storage = f"{file_id}_{original_filename}"
            item["file_id"] = file_id # Add file_id to item

            contents = await file.read()

            # Save locally
            local_path = documents_dir / original_filename # Use original filename for local save
            with local_path.open("wb") as f:
                f.write(contents)
            item["local_path"] = str(local_path)

            # Optional Supabase mirror
            if sb is not None:
                try:
                    res = sb.storage.from_(SUPABASE_BUCKET).upload(file.filename, contents)
                    item["supabase"] = {"path": getattr(res, "path", None) if res is not None else None}
                except Exception as sb_exc:
                    item["supabase"] = {"error": str(sb_exc)}

            # Ingest
            try:
                ingest_req = IngestRequest(filename=file.filename)
                ingest_result = await ingest_document(ingest_req)
                to_dict = getattr(ingest_result, "model_dump", None) or getattr(ingest_result, "dict")
                item["ingestion"] = to_dict()
                success_count += 1
            except Exception as ing_exc:
                item["ingestion"] = {"success": False, "error": str(ing_exc)}
                failure_count += 1

        except Exception as exc:
            item["error"] = str(exc)
            failure_count += 1

        results.append(item)

    return {
        "message": "Bulk upload completed",
        "successful": success_count,
        "failed": failure_count,
        "total": len(files),
        "results": results,
    }

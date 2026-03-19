import logging
from fastapi import APIRouter
from app.api.models.responses import HealthResponse
from app.config import settings

logger = logging.getLogger(__name__)

router = APIRouter(tags=["health"])

@router.get("/health", response_model=HealthResponse)
async def health_check():
    """Health check endpoint."""
    return HealthResponse(
        ok=True,
        message="CFC Animal Feed Software Chatbot API is running",
        version=settings.API_VERSION
    )

@router.get("/health/detailed")
async def detailed_health_check():
    """
    Detailed health check that verifies connectivity to external services.
    Returns per-service status so deployment issues can be quickly diagnosed.
    """
    checks = {}

    # 1. Pinecone connectivity
    try:
        if settings.PINECONE_API_KEY:
            from pinecone import Pinecone
            pc = Pinecone(api_key=settings.PINECONE_API_KEY)
            pc.list_indexes()
            checks["pinecone"] = {"status": "ok", "index": settings.PINECONE_INDEX_NAME}
        else:
            checks["pinecone"] = {"status": "not_configured", "detail": "PINECONE_API_KEY not set"}
    except Exception as e:
        logger.warning(f"Pinecone health check failed: {e}")
        checks["pinecone"] = {"status": "error", "detail": str(e)}

    # 2. Supabase connectivity
    try:
        if settings.SUPABASE_URL and settings.SUPABASE_SERVICE_ROLE_KEY:
            from app.core.supabase_service import supabase
            # A lightweight query to verify the connection
            supabase.table("profiles").select("id").limit(1).execute()
            checks["supabase"] = {"status": "ok", "url": settings.SUPABASE_URL}
        else:
            checks["supabase"] = {"status": "not_configured", "detail": "SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY not set"}
    except Exception as e:
        logger.warning(f"Supabase health check failed: {e}")
        checks["supabase"] = {"status": "error", "detail": str(e)}

    # 3. Gemini API key
    if settings.GEMINI_API_KEY:
        checks["gemini"] = {"status": "ok", "model": settings.GEMINI_MODEL}
    else:
        checks["gemini"] = {"status": "not_configured", "detail": "GEMINI_API_KEY not set"}

    # 4. Embedding model (sentence-transformers package availability)
    try:
        from sentence_transformers import SentenceTransformer  # noqa: F401
        checks["embedding_model"] = {"status": "ok", "model": settings.EMBED_MODEL_NAME}
    except ImportError as e:
        logger.warning(f"Embedding model health check failed: {e}")
        checks["embedding_model"] = {"status": "error", "detail": "sentence-transformers package not installed"}

    # Overall status
    all_ok = all(c.get("status") == "ok" for c in checks.values())

    return {
        "ok": all_ok,
        "version": settings.API_VERSION,
        "message": "All systems operational" if all_ok else "One or more services degraded",
        "services": checks,
    }
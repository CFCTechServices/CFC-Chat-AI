from fastapi import APIRouter
from app.api.models.responses import HealthResponse
from app.config import settings

router = APIRouter(tags=["health"])

@router.get("/health", response_model=HealthResponse)
async def health_check():
    """Health check endpoint."""
    return HealthResponse(
        ok=True,
        message="CFC Animal Feed Software Chatbot API is running",
        version=settings.API_VERSION
    )

@router.get("/health/detailed", response_model=HealthResponse)
async def detailed_health_check():
    """Detailed health check endpoint."""
    # In a production environment, you might check:
    # - Database connectivity
    # - External service availability
    # - System resources
    
    return HealthResponse(
        ok=True,
        message="All systems operational",
        version=settings.API_VERSION
    )
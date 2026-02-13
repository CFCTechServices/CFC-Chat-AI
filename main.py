"""
CFC Animal Feed Software Chatbot API
Main FastAPI application with organized structure
"""
from fastapi.staticfiles import StaticFiles
from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
import logging
from pathlib import Path
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent
load_dotenv(BASE_DIR / ".env")


# Import the organized modules
from app.config import settings
from app.api.endpoints import health, ingest, chat, visibility, videos, auth, sessions, admin, profile

from app.api.endpoints.upload import router as upload_router

#app = FastAPI()

#app.include_router(upload_router, prefix="/files", tags=["Files"])
# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Initialize FastAPI app
app = FastAPI(
    title=settings.API_TITLE,
    version=settings.API_VERSION,
    description="AI-powered help chatbot for animal-feed software with document search and Q&A capabilities"
)

WEB_DIR = BASE_DIR / "web"
app.mount(
    "/ui",
    StaticFiles(directory=WEB_DIR, html=True),
    name="ui",
)

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # TODO: Restrict to specific origins in production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
app.include_router(health.router)
app.include_router(ingest.router)
app.include_router(chat.router, prefix="/api/chat")
app.include_router(visibility.router)
app.include_router(upload_router, prefix="/files", tags=["Files"])
app.include_router(videos.router)
app.include_router(auth.router, prefix="/api/auth")
app.include_router(sessions.router, prefix="/api/chat")
app.include_router(admin.router, prefix="/api/admin")
app.include_router(profile.router, prefix="/api/profile")





@app.on_event("startup")
async def startup_event():
    """Initialize services on startup."""
    logger.info("Starting CFC Animal Feed Software Chatbot API")
    
    # Create data directories if they don't exist
    settings.DATA_DIR.mkdir(exist_ok=True)
    settings.DOCUMENTS_DIR.mkdir(exist_ok=True)
    settings.VIDEOS_DIR.mkdir(exist_ok=True)
    settings.PROCESSED_DIR.mkdir(exist_ok=True)
    
    # Create subdirectories
    (settings.DOCUMENTS_DIR / "docx").mkdir(exist_ok=True)
    (settings.DOCUMENTS_DIR / "doc").mkdir(exist_ok=True)
    (settings.VIDEOS_DIR / "transcripts").mkdir(exist_ok=True)
    
    logger.info("Data directories initialized")
    logger.info(f"API running at http://{settings.API_HOST}:{settings.API_PORT}")
    logger.info(f"API documentation available at http://{settings.API_HOST}:{settings.API_PORT}/docs")

# ---------- SPA catch-all (must be AFTER all API routes) ----------
# Client-side routes like /chat, /admin, /settings etc. need to serve
# index.html so the React SPA can boot and handle the route itself.
_SPA_ROUTES = {"", "chat", "admin", "settings", "history", "docs", "login", "transition"}

@app.get("/{full_path:path}")
async def serve_spa(full_path: str):
    """Serve index.html for client-side routes so page reloads work."""
    first_segment = full_path.strip("/").split("/")[0]
    if first_segment in _SPA_ROUTES:
        return FileResponse(WEB_DIR / "index.html")
    raise HTTPException(status_code=404, detail="Not Found")


@app.on_event("shutdown")
async def shutdown_event():
    """Cleanup on shutdown."""
    logger.info("Shutting down CFC Animal Feed Software Chatbot API")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.API_HOST,
        port=settings.API_PORT,
        reload=True,
        log_level="info"
    )

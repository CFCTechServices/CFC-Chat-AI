"""
CFC Animal Feed Software Chatbot API
Main FastAPI application with organized structure
"""
from fastapi.staticfiles import StaticFiles
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
import logging
from pathlib import Path
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent
load_dotenv(BASE_DIR / ".env")


# Import the organized modules
from app.config import settings
from app.api.endpoints import health, ingest, chat, visibility, videos, auth, sessions

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
    allow_origins=["*"],  # Configure appropriately for production
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
# Modify chat router prefix if needed, or keep as is. 
# The existing chat.py router didn't have a prefix in main.py but presumably had paths.
# Let's check chat.py paths.
# /search, /ask, /message 
# The user asked for /api/chat/message. 
# If I use prefix /api/chat for sessions, and chat.router is included safely...
# Let's see how chat.router was included:
# app.include_router(chat.router) -> paths are /search, /ask, /message
# User Request: /api/chat/message
# So I should probably move chat.router to be under /api/chat as well? 
# Or just rename the endpoint in chat.py?
# existing: @router.post("/message")
# if I include router with prefix="/api/chat" it becomes /api/chat/message. Perfect.
# BUT existing endpoints /search, /ask would become /api/chat/search, /api/chat/ask.
# Compatibility? 
# "Refactor ... to support this new architecture"
# I will enforce the new prefix for the chat router too to match the request style.
# Wait, the user ONLY specified /api/chat/message, /api/chat/sessions. 
# They didn't say /api/chat/search. 
# But it makes sense to group them.

# Admin Invite Endpoint
from pydantic import BaseModel
from fastapi import Depends
from app.core.auth import get_current_admin
from app.core.supabase_service import supabase
import uuid

class InviteResponse(BaseModel):
    code: str
    url: str

@app.post("/api/admin/invite", response_model=InviteResponse)
async def generate_invite(admin: dict = Depends(get_current_admin)):
    """
    Generates a new user invitation code.
    Only accessible by admins.
    """
    # Generate code in Python (UUID)
    code = str(uuid.uuid4())
    
    # Insert into invitations table
    # We let Postgres generate the ID, but we provide the code.
    data = {
        "code": code,
        "created_by": admin.id
    }
    
    # Perform insertion
    response = supabase.table("invitations").insert(data).execute()
    
    # Construct URL (placeholder domain as requested)
    url = f"https://my-app.com/?invite={code}"
    
    return InviteResponse(code=code, url=url)



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

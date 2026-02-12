from pathlib import Path
import os
from typing import Optional

class Settings:
    # Project paths
    PROJECT_ROOT = Path(__file__).parent.parent
    DATA_DIR = PROJECT_ROOT / "data"
    DOCUMENTS_DIR = DATA_DIR / "documents"
    VIDEOS_DIR = DATA_DIR / "videos"
    PROCESSED_DIR = DATA_DIR / "processed"
    
    # API Settings
    API_TITLE = "CFC Animal Feed Software Chatbot API"
    API_VERSION = "1.0.0"
    API_HOST = "0.0.0.0"
    API_PORT = 8000
    
    # Model Settings
    EMBED_MODEL_NAME = "sentence-transformers/all-MiniLM-L6-v2"
    EMBED_DIMENSION = 384
    
    # Chunking Settings
    CHUNK_SIZE = 600
    CHUNK_OVERLAP = 120
    
    # Pinecone Settings
    PINECONE_API_KEY: Optional[str] = os.getenv("PINECONE_API_KEY")
    PINECONE_INDEX_NAME = os.getenv("PINECONE_INDEX_NAME", "cfc-animal-feed-chatbot")
    PINECONE_CLOUD = os.getenv("PINECONE_CLOUD", "aws")
    PINECONE_REGION = os.getenv("PINECONE_REGION", "us-east-1")
    USE_PINECONE = bool(PINECONE_API_KEY)  # Fallback flag to disable Pinecone if no API key
    PINECONE_VIDEO_INDEX_NAME = (
        os.getenv("PINECONE_VIDEO_INDEX_NAME")
        or os.getenv("PINECONE_INDEX_NAME_VIDEOS")
        or os.getenv("PINECONE_INDEX")
        or PINECONE_INDEX_NAME
    )
    PINECONE_NAMESPACE: Optional[str] = os.getenv("PINECONE_NAMESPACE")
    
    # Supabase / Content Storage Settings
    # IMPORTANT: Two different keys for different purposes!
    SUPABASE_URL: Optional[str] = os.getenv("SUPABASE_URL")
    
    # ANON KEY - Public key for client-side authentication
    # - Safe to expose to frontend
    # - Respects Row Level Security (RLS)
    # - Used for: User authentication, client-facing operations
    SUPABASE_ANON_KEY: Optional[str] = os.getenv("SUPABASE_ANON_KEY")
    
    # SERVICE ROLE KEY - Admin key for backend operations
    # - NEVER expose to frontend/client
    # - Bypasses Row Level Security (RLS)
    # - Used for: Admin endpoints, server-side operations, bypassing RLS
    SUPABASE_SERVICE_ROLE_KEY: Optional[str] = os.getenv("SUPABASE_SERVICE_ROLE_KEY")
    
    SUPABASE_BUCKET: Optional[str] = os.getenv("SUPABASE_BUCKET")
    SUPABASE_BUCKET_VIDEOS: Optional[str] = os.getenv("SUPABASE_BUCKET_VIDEOS", SUPABASE_BUCKET)
    LOCAL_CONTENT_ROOT = PROCESSED_DIR / "content_repository"
    # OpenAI Settings (for future GPT integration)
    OPENAI_API_KEY: Optional[str] = os.getenv("OPENAI_API_KEY")
    OPENAI_MODEL = "gpt-3.5-turbo"

    # Gemini Settings (alternative to OpenAI)
    GEMINI_API_KEY: Optional[str] = os.getenv("GEMINI_API_KEY")
    GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.0-flash")
    
    # Resend API Configuration (for email invitations)
    RESEND_API_KEY: Optional[str] = os.getenv("RESEND_API_KEY")
    FRONTEND_BASE_URL: str = os.getenv("FRONTEND_BASE_URL", "http://localhost:3000")
    
    # Email Configuration
    # Set to False to create invitations without sending emails (useful when Resend domain is not verified)
    ENABLE_EMAIL_INVITES: bool = os.getenv("ENABLE_EMAIL_INVITES", "true").lower() in ("true", "1", "yes")
    
    # Search Settings
    DEFAULT_TOP_K = 5
    MAX_CONTEXT_LENGTH = 4000

settings = Settings()



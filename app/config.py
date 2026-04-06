import logging
from pathlib import Path
import os
from typing import Optional

logger = logging.getLogger(__name__)

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
    PINECONE_INDEX_NAME = os.getenv("PINECONE_INDEX_NAME", "cfc-rag-chatbot")
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
    # Azure OpenAI Settings
    # AZURE_OPENAI_ENDPOINT: e.g. https://<resource>.openai.azure.com/
    # AZURE_OPENAI_DEPLOYMENT: the deployment name set in Azure portal (e.g. gpt-4o-mini)
    AZURE_OPENAI_API_KEY: Optional[str] = os.getenv("AZURE_OPENAI_API_KEY")
    AZURE_OPENAI_ENDPOINT: Optional[str] = os.getenv("AZURE_OPENAI_ENDPOINT")
    AZURE_OPENAI_DEPLOYMENT: Optional[str] = os.getenv("AZURE_OPENAI_DEPLOYMENT")
    AZURE_OPENAI_API_VERSION: str = os.getenv("AZURE_OPENAI_API_VERSION", "2024-08-01-preview")

    def model_post_init(self, __context: object) -> None:
        if self.AZURE_OPENAI_API_KEY and self.AZURE_OPENAI_ENDPOINT and not self.AZURE_OPENAI_DEPLOYMENT:
            logger.warning(
                "AZURE_OPENAI_API_KEY and AZURE_OPENAI_ENDPOINT are set but "
                "AZURE_OPENAI_DEPLOYMENT is missing — LLM answers will be disabled "
                "until AZURE_OPENAI_DEPLOYMENT is configured."
            )


    # Resend API Configuration (for email invitations)
    RESEND_API_KEY: Optional[str] = os.getenv("RESEND_API_KEY")
    FRONTEND_BASE_URL: str = os.getenv("FRONTEND_BASE_URL", "http://localhost:8000/ui")
    
    # Email Configuration
    # Set to False to create invitations without sending emails (useful when Resend domain is not verified)
    ENABLE_EMAIL_INVITES: bool = os.getenv("ENABLE_EMAIL_INVITES", "true").lower() in ("true", "1", "yes")
    
    # Search Settings
    DEFAULT_TOP_K = 5
    MAX_CONTEXT_LENGTH = 4000

    # ── Feedback Re-Ranking Settings ──────────────────────────────────────────
    # FEEDBACK_ENABLED: set "false" to disable all feedback re-ranking.
    FEEDBACK_ENABLED: bool = os.getenv("FEEDBACK_ENABLED", "true").lower() in ("true", "1", "yes")

    # Phase 1 – Global score boost
    # FEEDBACK_ALPHA: max boost/penalty fraction applied by the global signal.
    #   With tanh the multiplier is bounded to (1 − alpha, 1 + alpha).
    #   Default 0.3 → ±30 % max from global votes alone.
    FEEDBACK_ALPHA: float = float(os.getenv("FEEDBACK_ALPHA", "0.3"))
    # FEEDBACK_SCALE: controls how quickly the global tanh saturates.
    #   Lower value → saturates faster (fewer votes needed to hit the ceiling).
    FEEDBACK_SCALE: float = float(os.getenv("FEEDBACK_SCALE", "5.0"))

    # Phase 2 – Query-aware score boost
    # FEEDBACK_ALPHA_QUERY: max boost/penalty fraction applied by the
    #   query-aware signal.  Kept smaller (0.15) so that a single highly-
    #   similar past query can nudge the ranking without overwhelming it.
    #   Combined ceiling with Phase 1: (1 − 0.30 − 0.15, 1 + 0.30 + 0.15)
    #   = (0.55, 1.45) — still tight.
    FEEDBACK_ALPHA_QUERY: float = float(os.getenv("FEEDBACK_ALPHA_QUERY", "0.15"))
    # FEEDBACK_SCALE_QUERY: tanh saturation scale for the query-aware signal.
    #   The weighted score is a float (sum of rating × similarity), so it
    #   saturates at smaller absolute values than the integer global net score.
    FEEDBACK_SCALE_QUERY: float = float(os.getenv("FEEDBACK_SCALE_QUERY", "3.0"))
    # FEEDBACK_SIM_THRESHOLD: minimum cosine similarity between the stored
    #   query embedding and the current query for an event to be counted.
    #   0.75 means only very similar questions trigger the boost.
    FEEDBACK_SIM_THRESHOLD: float = float(os.getenv("FEEDBACK_SIM_THRESHOLD", "0.75"))

settings = Settings()



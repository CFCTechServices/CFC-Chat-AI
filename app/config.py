from pathlib import Path
from typing import Optional, List

from pydantic import computed_field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Project root is two levels up from this file (app/config.py → app/ → project root)
_PROJECT_ROOT = Path(__file__).parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # ---------------------------------------------------------------------------
    # API Settings
    # ---------------------------------------------------------------------------
    API_TITLE: str = "CFC Animal Feed Software Chatbot API"
    API_VERSION: str = "1.0.0"
    API_HOST: str = "0.0.0.0"
    API_PORT: int = 8000

    # ---------------------------------------------------------------------------
    # CORS
    # Comma-separated list of allowed origins.
    # In production set to your domain(s), e.g. "https://example.com"
    # For local dev the default allows the FastAPI server itself.
    # ---------------------------------------------------------------------------
    CORS_ORIGINS: str = "http://localhost:8000"

    # ---------------------------------------------------------------------------
    # Model / Embedding Settings
    # ---------------------------------------------------------------------------
    EMBED_MODEL_NAME: str = "sentence-transformers/all-MiniLM-L6-v2"
    EMBED_DIMENSION: int = 384

    # ---------------------------------------------------------------------------
    # Chunking Settings
    # ---------------------------------------------------------------------------
    CHUNK_SIZE: int = 600
    CHUNK_OVERLAP: int = 120

    # ---------------------------------------------------------------------------
    # Pinecone Settings
    # ---------------------------------------------------------------------------
    PINECONE_API_KEY: Optional[str] = None
    PINECONE_INDEX_NAME: str = "cfc-animal-feed-chatbot"
    PINECONE_CLOUD: str = "aws"
    PINECONE_REGION: str = "us-east-1"
    # Video index — resolved via fallback chain in model_validator below
    PINECONE_VIDEO_INDEX_NAME: Optional[str] = None
    PINECONE_INDEX_NAME_VIDEOS: Optional[str] = None
    PINECONE_INDEX: Optional[str] = None
    PINECONE_NAMESPACE: Optional[str] = None

    # ---------------------------------------------------------------------------
    # Supabase / Content Storage Settings
    # ---------------------------------------------------------------------------
    SUPABASE_URL: Optional[str] = None

    # ANON KEY — safe to expose to the frontend; respects Row Level Security
    SUPABASE_ANON_KEY: Optional[str] = None

    # SERVICE ROLE KEY — backend only; bypasses Row Level Security; never expose
    SUPABASE_SERVICE_ROLE_KEY: Optional[str] = None

    SUPABASE_BUCKET: Optional[str] = None
    # Defaults to SUPABASE_BUCKET when not explicitly set (resolved in validator)
    SUPABASE_BUCKET_VIDEOS: Optional[str] = None

    # ---------------------------------------------------------------------------
    # OpenAI Settings
    # ---------------------------------------------------------------------------
    OPENAI_API_KEY: Optional[str] = None
    OPENAI_MODEL: str = "gpt-3.5-turbo"

    # ---------------------------------------------------------------------------
    # Gemini Settings
    # ---------------------------------------------------------------------------
    GEMINI_API_KEY: Optional[str] = None
    GEMINI_MODEL: str = "gemini-2.0-flash"

    # ---------------------------------------------------------------------------
    # Search Settings
    # ---------------------------------------------------------------------------
    DEFAULT_TOP_K: int = 5
    MAX_CONTEXT_LENGTH: int = 4000

    # ---------------------------------------------------------------------------
    # Validators — resolve cross-field defaults
    # ---------------------------------------------------------------------------
    @model_validator(mode="after")
    def _resolve_computed_defaults(self) -> "Settings":
        # SUPABASE_BUCKET_VIDEOS defaults to SUPABASE_BUCKET when not set
        if not self.SUPABASE_BUCKET_VIDEOS and self.SUPABASE_BUCKET:
            self.SUPABASE_BUCKET_VIDEOS = self.SUPABASE_BUCKET

        # PINECONE_VIDEO_INDEX_NAME fallback chain
        if not self.PINECONE_VIDEO_INDEX_NAME:
            self.PINECONE_VIDEO_INDEX_NAME = (
                self.PINECONE_INDEX_NAME_VIDEOS
                or self.PINECONE_INDEX
                or self.PINECONE_INDEX_NAME
            )

        return self

    # ---------------------------------------------------------------------------
    # Computed fields (read-only, not loaded from env)
    # ---------------------------------------------------------------------------
    @computed_field
    @property
    def USE_PINECONE(self) -> bool:
        return bool(self.PINECONE_API_KEY)

    @computed_field
    @property
    def cors_origins_list(self) -> List[str]:
        """Parse the CORS_ORIGINS comma-separated string into a list."""
        return [o.strip() for o in self.CORS_ORIGINS.split(",") if o.strip()]

    @computed_field
    @property
    def DATA_DIR(self) -> Path:
        return _PROJECT_ROOT / "data"

    @computed_field
    @property
    def DOCUMENTS_DIR(self) -> Path:
        return self.DATA_DIR / "documents"

    @computed_field
    @property
    def VIDEOS_DIR(self) -> Path:
        return self.DATA_DIR / "videos"

    @computed_field
    @property
    def PROCESSED_DIR(self) -> Path:
        return self.DATA_DIR / "processed"

    @computed_field
    @property
    def LOCAL_CONTENT_ROOT(self) -> Path:
        return self.PROCESSED_DIR / "content_repository"


settings = Settings()

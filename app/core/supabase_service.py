import os
from pathlib import Path
from supabase import create_client
from dotenv import load_dotenv

# Load environment variables
BASE_DIR = Path(__file__).resolve().parents[2]
load_dotenv(BASE_DIR / ".env")

SUPABASE_URL = os.getenv("SUPABASE_URL")

# Backend service uses SERVICE_ROLE_KEY for admin operations
# This key bypasses Row Level Security (RLS) for backend operations
SUPABASE_SERVICE_ROLE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY")

if not SUPABASE_URL or not SUPABASE_SERVICE_ROLE_KEY:
    raise RuntimeError(
        "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required for backend operations. "
        "SERVICE_ROLE_KEY is needed to bypass RLS for admin operations."
    )

SUPABASE_BUCKET = os.getenv("SUPABASE_BUCKET")

# Create backend Supabase client with SERVICE_ROLE_KEY
# This client has admin privileges and bypasses RLS
supabase = create_client(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY)

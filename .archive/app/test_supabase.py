from supabase import create_client
import os
from pathlib import Path
from dotenv import load_dotenv

# Load .env from the project root
BASE_DIR = Path(__file__).resolve().parents[1]
load_dotenv(BASE_DIR / ".env")

url = os.getenv("SUPABASE_URL")
key = os.getenv("SUPABASE_ANON_KEY")
bucket = os.getenv("SUPABASE_BUCKET")

print("Loaded URL:", url)  # 👈 Add this to confirm it's loading correctly

supabase = create_client(url, key)

with open("../README.md", "rb") as f:
    res = supabase.storage.from_(bucket).upload("test-readme.md", f)
    print(res)

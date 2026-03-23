import sys
from pathlib import Path
from dotenv import load_dotenv

_project_root = Path(__file__).resolve().parents[1]
load_dotenv(_project_root / ".env")
sys.path.insert(0, str(_project_root))

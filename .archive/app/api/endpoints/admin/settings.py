from fastapi import APIRouter, HTTPException, Depends
import logging
import json
from app.core.auth import get_current_admin
from app.config import settings
from .models import AdminSettings, AdminSettingsUpdate

logger = logging.getLogger(__name__)
router = APIRouter()

SETTINGS_FILE = settings.DATA_DIR / "admin_settings.json"


def _load_settings() -> AdminSettings:
    if SETTINGS_FILE.exists():
        try:
            data = json.loads(SETTINGS_FILE.read_text())
            return AdminSettings(**data)
        except Exception:
            logger.warning("Corrupt admin_settings.json, using defaults")
    return AdminSettings()


def _save_settings(s: AdminSettings) -> None:
    SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS_FILE.write_text(json.dumps(s.model_dump(), indent=2))


@router.get("/settings", response_model=AdminSettings)
async def get_settings(admin: dict = Depends(get_current_admin)):
    return _load_settings()

@router.patch("/settings", response_model=AdminSettings)
async def update_settings(updates: AdminSettingsUpdate, admin: dict = Depends(get_current_admin)):
    current = _load_settings()
    patch = updates.model_dump(exclude_none=True)
    if not patch:
        raise HTTPException(status_code=400, detail="No settings provided")
    updated = current.model_copy(update=patch)
    _save_settings(updated)
    logger.info(f"Admin {admin.id} updated settings: {patch}")
    return updated

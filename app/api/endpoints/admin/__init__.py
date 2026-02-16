from fastapi import APIRouter
from .invitations import router as invitations_router
from .users import router as users_router
from .settings import router as settings_router
from .ingestion import router as ingestion_router

router = APIRouter(tags=["admin"])
router.include_router(invitations_router)
router.include_router(users_router)
router.include_router(settings_router)
router.include_router(ingestion_router)

from fastapi import APIRouter
from .invitations import router as invitations_router
from .users import router as users_router
from .settings import router as settings_router
from .documents import router as documents_router

router = APIRouter(tags=["admin"])
router.include_router(invitations_router)
router.include_router(users_router)
router.include_router(settings_router)
router.include_router(documents_router)

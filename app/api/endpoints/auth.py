import logging
import requests as http_requests
from fastapi import APIRouter
from pydantic import BaseModel

logger = logging.getLogger(__name__)
router = APIRouter(tags=["auth"])

@router.get("/config")
def get_auth_config():
    """
    Returns public Supabase configuration for the frontend.
    """
    from app.config import settings
    return {
        "supabaseUrl": settings.SUPABASE_URL,
        "supabaseKey": settings.SUPABASE_ANON_KEY
    }


class ForgotPasswordRequest(BaseModel):
    email: str

class ForgotPasswordResponse(BaseModel):
    success: bool
    message: str

@router.post("/forgot-password", response_model=ForgotPasswordResponse)
async def forgot_password(request: ForgotPasswordRequest):
    """
    Sends a password reset email via Supabase Auth.
    Always returns success to prevent email enumeration.
    """
    from app.config import settings

    try:
        redirect_url = settings.FRONTEND_BASE_URL.rstrip("/")
        resp = http_requests.post(
            f"{settings.SUPABASE_URL}/auth/v1/recover",
            json={"email": request.email},
            headers={
                "apikey": settings.SUPABASE_SERVICE_ROLE_KEY,
                "Authorization": f"Bearer {settings.SUPABASE_SERVICE_ROLE_KEY}",
                "Content-Type": "application/json",
            },
            params={"redirect_to": redirect_url},
        )
        if resp.status_code == 429:
            logger.warning(f"Rate limited by Supabase for password reset: {resp.text}")
            return ForgotPasswordResponse(
                success=False,
                message="Too many reset attempts. Please wait a few minutes before trying again.",
            )
        if resp.status_code >= 400:
            logger.warning(f"Supabase recover returned {resp.status_code}: {resp.text}")
        else:
            logger.info(f"Password reset email requested for {request.email}")
    except Exception as e:
        logger.error(f"Error sending password reset email: {e}")

    return ForgotPasswordResponse(
        success=True,
        message="If an account exists with this email, a password reset link has been sent.",
    )

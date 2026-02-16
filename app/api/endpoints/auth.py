import logging
import requests as http_requests
from fastapi import APIRouter, HTTPException
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


class ValidateInviteRequest(BaseModel):
    invite_code: str

class ValidateInviteResponse(BaseModel):
    valid: bool
    message: str
    email: str = None  # Email associated with the invite code


@router.post("/validate-invite", response_model=ValidateInviteResponse)
async def validate_invite(request: ValidateInviteRequest):
    """
    Validates an invite code and returns the associated email.
    """
    from app.core.supabase_service import supabase

    try:
        # Query the invitations table to get the code and email
        response = supabase.table("invitations").select("email, is_used").eq("code", request.invite_code).single().execute()

        if response.data and not response.data.get("is_used"):
            return ValidateInviteResponse(
                valid=True,
                message="Invite code is valid.",
                email=response.data.get("email")
            )
        elif response.data and response.data.get("is_used"):
            return ValidateInviteResponse(valid=False, message="Invite code has already been used.")
        else:
            return ValidateInviteResponse(valid=False, message="Invalid invite code.")
    except Exception as e:
        logger.error(f"Error validating invite: {e}")
        return ValidateInviteResponse(valid=False, message="Invalid invite code.")


class MarkInviteUsedRequest(BaseModel):
    invite_code: str

@router.post("/mark-invite-used")
async def mark_invite_used(request: MarkInviteUsedRequest):
    """
    Marks an invite code as used after successful account creation.
    """
    from app.core.supabase_service import supabase

    try:
        response = (
            supabase.table("invitations")
            .update({"is_used": True})
            .eq("code", request.invite_code)
            .eq("is_used", False)
            .execute()
        )
        if response.data:
            return {"success": True, "message": "Invite marked as used."}
        else:
            return {"success": False, "message": "Invite code not found or already used."}
    except Exception as e:
        logger.error(f"Error marking invite as used: {e}")
        raise HTTPException(status_code=500, detail="Failed to mark invite as used.")

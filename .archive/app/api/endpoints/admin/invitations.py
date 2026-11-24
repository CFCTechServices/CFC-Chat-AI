from fastapi import APIRouter, HTTPException, Depends
import logging
from datetime import datetime, timezone
from app.core.auth import get_current_admin
from app.core.supabase_service import supabase
from app.config import settings
from .models import InviteRequest, InviteResponse, InvitationStatusResponse

logger = logging.getLogger(__name__)
router = APIRouter()


@router.post("/invite", response_model=InviteResponse)
async def generate_invite(request: InviteRequest, admin: dict = Depends(get_current_admin)):
    """
    Add an email to the invitation whitelist.
    Only accessible by admin users.

    Handles the partial unique index on (email) WHERE is_registered = false:
    - If an active (non-expired) unregistered invite exists, reject.
    - If an expired unregistered invite exists, mark it registered first, then create a new one.
    - Wraps insert in try/except to handle race conditions on the unique index.
    """
    try:
        # Step 1: Check if user already exists in the system
        existing_profile = supabase.table("profiles")\
            .select("id")\
            .eq("email", request.email)\
            .limit(1)\
            .execute()

        if existing_profile.data:
            raise HTTPException(
                status_code=409,
                detail="This user already exists in the system."
            )

        # Step 2: Check for existing unregistered invitation for this email
        existing = supabase.table("invitations")\
            .select("id, expires_at")\
            .eq("email", request.email)\
            .eq("is_registered", False)\
            .execute()

        if existing.data:
            row = existing.data[0]
            expires_at = datetime.fromisoformat(row["expires_at"].replace("Z", "+00:00"))
            now = datetime.now(timezone.utc)

            if expires_at > now:
                # Active invite exists — reject
                raise HTTPException(
                    status_code=409,
                    detail="There is already a pending invitation for this email."
                )
            else:
                # Expired invite — delete it so a fresh one can be created
                supabase.table("invitations")\
                    .delete()\
                    .eq("id", row["id"])\
                    .execute()
                logger.info(f"Deleted expired invitation {row['id']} for {request.email}")

        # Step 2: Insert new invitation (expires_at is a DB default)
        invitation_data = {
            "email": request.email,
            "created_by": admin.id
        }

        try:
            insert_response = supabase.table("invitations").insert(invitation_data).execute()
        except Exception as insert_error:
            error_msg = str(insert_error)
            if "idx_one_active_invite_per_email" in error_msg or "unique" in error_msg.lower():
                raise HTTPException(
                    status_code=409,
                    detail="An active invitation already exists for this email."
                )
            raise

        if not insert_response.data:
            raise HTTPException(status_code=500, detail="Failed to create invitation")

        new_invite = insert_response.data[0]
        new_expires_at = new_invite["expires_at"]

        logger.info(f"Invitation created for {request.email}, expires at {new_expires_at}")

        return InviteResponse(
            message=f"Invitation created for {request.email}",
            email=request.email,
            expires_at=new_expires_at
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to generate invitation: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to generate invitation: {e}")


@router.get("/invitations/status/{email}", response_model=InvitationStatusResponse)
async def get_invitation_status(email: str, admin: dict = Depends(get_current_admin)):
    """
    Return the current invitation state for an email address.
    Possible statuses: active, expired, registered, none.
    """
    try:
        # Get the most recent invitation for this email
        response = supabase.table("invitations")\
            .select("is_registered, expires_at")\
            .eq("email", email)\
            .order("created_at", desc=True)\
            .limit(1)\
            .execute()

        if not response.data:
            return InvitationStatusResponse(email=email, status="none")

        row = response.data[0]

        if row["is_registered"]:
            return InvitationStatusResponse(
                email=email,
                status="registered",
                expires_at=row["expires_at"]
            )

        expires_at = datetime.fromisoformat(row["expires_at"].replace("Z", "+00:00"))
        now = datetime.now(timezone.utc)

        if expires_at > now:
            return InvitationStatusResponse(
                email=email,
                status="active",
                expires_at=row["expires_at"]
            )
        else:
            return InvitationStatusResponse(
                email=email,
                status="expired",
                expires_at=row["expires_at"]
            )

    except Exception as e:
        logger.error(f"Failed to get invitation status for {email}: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to get invitation status: {e}")

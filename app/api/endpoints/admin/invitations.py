from fastapi import APIRouter, HTTPException, Depends
import logging
from datetime import datetime, timezone
from app.core.auth import get_current_admin
from app.core.supabase_service import supabase
from app.config import settings
from app.services.email_service import send_invite_email
from .models import InviteRequest, InviteResponse, InvitationStatusResponse

logger = logging.getLogger(__name__)
router = APIRouter()


@router.post("/invite", response_model=InviteResponse)
async def generate_invite(request: InviteRequest, admin: dict = Depends(get_current_admin)):
    """
    Generate a new invitation code and send it via email.
    Only accessible by admin users.

    Handles the partial unique index on (email) WHERE is_used = false:
    - If an active (non-expired) unused invite exists, reject.
    - If an expired unused invite exists, mark it used first, then create a new one.
    - Wraps insert in try/except to handle race conditions on the unique index.
    """
    try:
        # Step 1: Check for existing unused invitation for this email
        existing = supabase.table("invitations")\
            .select("id, code, expires_at")\
            .eq("email", request.email)\
            .eq("is_used", False)\
            .execute()

        if existing.data:
            row = existing.data[0]
            expires_at = datetime.fromisoformat(row["expires_at"].replace("Z", "+00:00"))
            now = datetime.now(timezone.utc)

            if expires_at > now:
                # Step 2: Active invite exists — reject
                raise HTTPException(
                    status_code=409,
                    detail="An active invitation already exists for this email."
                )
            else:
                # Step 3: Expired invite — mark as used to clear the unique index
                supabase.table("invitations")\
                    .update({"is_used": True})\
                    .eq("id", row["id"])\
                    .execute()
                logger.info(f"Marked expired invitation {row['id']} as used for {request.email}")

        # Step 4: Insert new invitation (code and expires_at are DB defaults)
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
        new_code = new_invite["code"]
        new_expires_at = new_invite["expires_at"]

        # Step 5: Generate invite URL and send email
        invite_url = f"{settings.FRONTEND_BASE_URL}/register?code={new_code}&email={request.email}"

        if settings.ENABLE_EMAIL_INVITES:
            try:
                email_sent = send_invite_email(request.email, new_code, invite_url)
                if email_sent:
                    logger.info(f"Email sent successfully to {request.email}. Response ID: {email_sent}")
                else:
                    logger.warning(f"Invitation created but email failed to send to {request.email}")
            except Exception as email_error:
                logger.error(f"Failed to send email to {request.email}: {email_error}")
        else:
            logger.info(f"Email sending disabled. Invitation created for {request.email} with code {new_code}")
            logger.info(f"Share this invite URL manually: {invite_url}")

        return InviteResponse(
            message=f"Invite sent to {request.email}" if settings.ENABLE_EMAIL_INVITES else f"Invite created for {request.email} (email disabled)",
            email=request.email,
            code=new_code,
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
    Possible statuses: active, expired, used, none.
    """
    try:
        # Get the most recent invitation for this email
        response = supabase.table("invitations")\
            .select("code, is_used, expires_at")\
            .eq("email", email)\
            .order("created_at", desc=True)\
            .limit(1)\
            .execute()

        if not response.data:
            return InvitationStatusResponse(email=email, status="none")

        row = response.data[0]

        if row["is_used"]:
            return InvitationStatusResponse(
                email=email,
                status="used",
                code=row["code"],
                expires_at=row["expires_at"]
            )

        expires_at = datetime.fromisoformat(row["expires_at"].replace("Z", "+00:00"))
        now = datetime.now(timezone.utc)

        if expires_at > now:
            return InvitationStatusResponse(
                email=email,
                status="active",
                code=row["code"],
                expires_at=row["expires_at"]
            )
        else:
            return InvitationStatusResponse(
                email=email,
                status="expired",
                code=row["code"],
                expires_at=row["expires_at"]
            )

    except Exception as e:
        logger.error(f"Failed to get invitation status for {email}: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to get invitation status: {e}")

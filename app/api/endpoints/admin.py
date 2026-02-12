from fastapi import APIRouter, HTTPException, Depends
from pydantic import BaseModel, EmailStr
import logging
import uuid
from datetime import datetime, timezone
from typing import Optional
from app.core.auth import get_current_admin
from app.core.supabase_service import supabase
from app.config import settings
from app.services.email_service import send_invite_email

logger = logging.getLogger(__name__)
router = APIRouter(tags=["admin"])

# Models
class InviteRequest(BaseModel):
    email: EmailStr

class InviteResponse(BaseModel):
    message: str
    email: str

class ChangeRoleRequest(BaseModel):
    user_id: str
    new_role: str

class ChangeRoleResponse(BaseModel):
    message: str
    user_id: str
    new_role: str

class DeactivateUserRequest(BaseModel):
    reason: Optional[str] = None

class DeactivateUserResponse(BaseModel):
    message: str
    user_id: str
    email: str
    status: str
    deleted_at: str

class ReactivateUserResponse(BaseModel):
    message: str
    user_id: str
    email: str
    status: str
    restored_at: str

class DeleteUserResponse(BaseModel):
    message: str
    user_id: str
    email: str
    permanently_deleted: bool = True

# Endpoints

@router.post("/invite", response_model=InviteResponse)
async def generate_invite(request: InviteRequest, admin: dict = Depends(get_current_admin)):
    """
    Generate a new invitation code and send it via email.
    Only accessible by admin users.
    """
    try:
        # Generate a unique invitation code
        code = uuid.uuid4()
        
        # Insert into invitations table
        invitation_data = {
            "code": str(code),
            "email": request.email,
            "created_by": admin.id
        }
        
        insert_response = supabase.table("invitations").insert(invitation_data).execute()
        
        if not insert_response.data:
            raise HTTPException(status_code=500, detail="Failed to create invitation")
        
        # Generate invite URL
        invite_url = f"{settings.FRONTEND_BASE_URL}/register?code={code}&email={request.email}"
        
        # Send email (only if enabled in settings)
        if settings.ENABLE_EMAIL_INVITES:
            try:
                email_sent = send_invite_email(request.email, str(code), invite_url)
                if email_sent:
                    logger.info(f"Email sent successfully to {request.email}. Response ID: {email_sent}")
                else:
                    logger.warning(f"Failed to send email to {request.email}")
                    print(f"Warning: Invitation created but email failed to send to {request.email}")
            except Exception as email_error:
                logger.error(f"Failed to send email to {request.email}: {email_error}")
                print(f"Failed to send email to {request.email}: {email_error}")
                print(f"Warning: Invitation created but email failed to send to {request.email}")
        else:
            logger.info(f"Email sending disabled. Invitation created for {request.email} with code {code}")
            print(f"ℹ️  Email sending is disabled. Invitation created successfully.")
            print(f"   Share this invite URL manually: {invite_url}")
        
        return InviteResponse(
            message=f"Invite sent to {request.email}" if settings.ENABLE_EMAIL_INVITES else f"Invite created for {request.email} (email disabled)",
            email=request.email
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to generate invitation: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to generate invitation: {e}")


@router.post("/change-role", response_model=ChangeRoleResponse)
async def change_user_role(request: ChangeRoleRequest, admin: dict = Depends(get_current_admin)):
    """
    Change the role of a user. Only accessible by admins.
    Allowed roles: 'user', 'dev', 'admin'
    """
    allowed_roles = ['user', 'dev', 'admin']
    
    # Validate role
    if request.new_role not in allowed_roles:
        raise HTTPException(
            status_code=400, 
            detail=f"Invalid role. Allowed roles are: {', '.join(allowed_roles)}"
        )
    
    try:
        # Check if user exists
        user_check = supabase.table("profiles").select("id, role").eq("id", request.user_id).execute()
        if not user_check.data:
            raise HTTPException(status_code=404, detail="User not found")
        
        current_role = user_check.data[0].get("role")
        
        # Update the user's role
        update_response = supabase.table("profiles").update({
            "role": request.new_role
        }).eq("id", request.user_id).execute()
        
        if not update_response.data:
            raise HTTPException(status_code=500, detail="Failed to update user role")
        
        logger.info(f"Admin {admin.id} changed user {request.user_id} role from {current_role} to {request.new_role}")
        
        return ChangeRoleResponse(
            message=f"Successfully changed user role to {request.new_role}",
            user_id=request.user_id,
            new_role=request.new_role
        )
    
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to change user role: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to change user role: {e}")

@router.post("/users/{user_id}/deactivate", response_model=DeactivateUserResponse)
async def deactivate_user(
    user_id: str,
    request: DeactivateUserRequest = DeactivateUserRequest(),
    admin: dict = Depends(get_current_admin)
):
    """
    Deactivate a user account. Sets status to 'inactive' and records deactivation metadata.
    """
    # Prevent self-deactivation
    if user_id == admin.id:
        raise HTTPException(
            status_code=400,
            detail="Cannot deactivate your own account"
        )
    
    try:
        # Check if user exists
        user_check = supabase.table("profiles")\
            .select("id, status, deleted_at")\
            .eq("id", user_id)\
            .execute()
        
        if not user_check.data:
            raise HTTPException(status_code=404, detail="User not found")
        
        # Check if already deactivated
        current_status = user_check.data[0].get("status")
        if current_status == "inactive":
            raise HTTPException(
                status_code=400,
                detail="User is already deactivated"
            )
        
        # Get user email from Supabase Auth
        try:
            auth_user = supabase.auth.admin.get_user_by_id(user_id)
            user_email = auth_user.user.email if auth_user.user else "unknown"
        except:
            user_email = "unknown"
        
        # Step 1: Ban user in Supabase Auth to prevent new logins
        try:
            supabase.auth.admin.update_user_by_id(
                user_id,
                {"ban_duration": "876000h"}  # Ban for ~100 years (effectively permanent until reactivated)
            )
            logger.info(f"Banned user {user_id} in Supabase Auth")
        except Exception as ban_error:
            logger.warning(f"Failed to ban user in Auth (continuing anyway): {ban_error}")
        
        # Step 2: Sign out all sessions to invalidate existing JWT tokens
        try:
            supabase.auth.admin.sign_out(user_id, scope="global")
            logger.info(f"Signed out all sessions for user {user_id}")
        except Exception as signout_error:
            logger.warning(f"Failed to sign out user sessions (continuing anyway): {signout_error}")
        
        # Step 3: Update profile with inactive status
        now = datetime.now(timezone.utc).isoformat()
        update_response = supabase.table("profiles").update({
            "status": "inactive",
            "deleted_at": now,
            "deleted_by": admin.id
        }).eq("id", user_id).execute()
        
        if not update_response.data:
            raise HTTPException(
                status_code=500,
                detail="Failed to deactivate user"
            )
        
        # Log the action
        reason_text = f"Reason: {request.reason}" if request.reason else "No reason provided"
        logger.info(
            f"Admin {admin.id} deactivated user {user_id} ({user_email}). {reason_text}"
        )
        
        return DeactivateUserResponse(
            message=f"User {user_email} has been deactivated",
            user_id=user_id,
            email=user_email,
            status="inactive",
            deleted_at=now
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to deactivate user {user_id}: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to deactivate user: {str(e)}"
        )

@router.post("/users/{user_id}/reactivate", response_model=ReactivateUserResponse)
async def reactivate_user(
    user_id: str,
    admin: dict = Depends(get_current_admin)
):
    """
    Reactivate a previously deactivated user account.
    Reverses the deactivation by setting status back to 'active'.
    """
    try:
        # Check if user exists
        user_check = supabase.table("profiles")\
            .select("id, status, deleted_at")\
            .eq("id", user_id)\
            .execute()
        
        if not user_check.data:
            raise HTTPException(status_code=404, detail="User not found")
        
        # Check if user is actually inactive
        current_status = user_check.data[0].get("status")
        if current_status != "inactive":
            raise HTTPException(
                status_code=400,
                detail="User is not deactivated"
            )
        
        # Get user email from Supabase Auth
        try:
            auth_user = supabase.auth.admin.get_user_by_id(user_id)
            user_email = auth_user.user.email if auth_user.user else "unknown"
        except:
            user_email = "unknown"
        
        # Step 1: Unban user in Supabase Auth to allow new logins
        try:
            supabase.auth.admin.update_user_by_id(
                user_id,
                {"ban_duration": "none"}  # Remove the ban
            )
            logger.info(f"Unbanned user {user_id} in Supabase Auth")
        except Exception as unban_error:
            logger.warning(f"Failed to unban user in Auth (continuing anyway): {unban_error}")
        
        # Step 2: Restore to active status in profile
        now = datetime.now(timezone.utc).isoformat()
        update_response = supabase.table("profiles").update({
            "status": "active",
            "deleted_at": None,
            "deleted_by": None
        }).eq("id", user_id).execute()
        
        if not update_response.data:
            raise HTTPException(
                status_code=500,
                detail="Failed to reactivate user"
            )
        
        # Log the action
        logger.info(
            f"Admin {admin.id} reactivated user {user_id} ({user_email}). "
            f"User has been restored to active status."
        )
        
        return ReactivateUserResponse(
            message=f"User {user_email} has been reactivated",
            user_id=user_id,
            email=user_email,
            status="active",
            restored_at=now
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to reactivate user {user_id}: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to reactivate user: {str(e)}"
        )

@router.delete("/users/{user_id}", response_model=DeleteUserResponse)
async def delete_user(
    user_id: str,
    admin: dict = Depends(get_current_admin)
):
    """
    PERMANENTLY delete a user. This removes the user from auth.users,
    which triggers CASCADE deletion of all related data:
    - Profile (public.profiles)
    - Chat sessions (public.chat_sessions)
    - Chat messages (public.chat_messages)
    - Invitations are set to NULL (preserved but anonymized)
    - Feedback is set to NULL (preserved but anonymized)
    
    WARNING: This action CANNOT be undone!
    """
    # Prevent self-deletion
    if user_id == admin.id:
        raise HTTPException(
            status_code=400,
            detail="Cannot delete your own account"
        )
    
    try:
        # Check if user exists
        user_check = supabase.table("profiles")\
            .select("id")\
            .eq("id", user_id)\
            .execute()
        
        if not user_check.data:
            raise HTTPException(status_code=404, detail="User not found")
        
        # Get user email before deletion
        try:
            auth_user = supabase.auth.admin.get_user_by_id(user_id)
            user_email = auth_user.user.email if auth_user.user else "unknown"
        except:
            user_email = "unknown"
        
        # HARD DELETE: Remove from auth.users
        # This triggers CASCADE and removes:
        # - public.profiles (ON DELETE CASCADE)
        # - public.chat_sessions (ON DELETE CASCADE)
        # - Related chat_messages (via session CASCADE)
        # - Sets NULL on invitations and feedback (ON DELETE SET NULL)
        supabase.auth.admin.delete_user(user_id)
        
        # Log the action
        logger.warning(
            f"Admin {admin.id} PERMANENTLY DELETED user {user_id} ({user_email}). "
            f"All profile and chat data has been removed via CASCADE."
        )
        
        return DeleteUserResponse(
            message=f"User {user_email} has been permanently deleted",
            user_id=user_id,
            email=user_email,
            permanently_deleted=True
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete user {user_id}: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete user: {str(e)}"
        )

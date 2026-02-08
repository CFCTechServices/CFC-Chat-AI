from fastapi import APIRouter, HTTPException, Depends
from pydantic import BaseModel, EmailStr
import logging
import uuid
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

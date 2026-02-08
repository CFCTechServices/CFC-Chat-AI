from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from app.core.auth import check_invite_code

router = APIRouter(tags=["auth"])

class ValidateInviteRequest(BaseModel):
    invite_code: str

class ValidateInviteResponse(BaseModel):
    valid: bool
    message: str
    email: str = None  # Email associated with the invite code

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
        print(f"Error validating invite: {e}")
        return ValidateInviteResponse(valid=False, message="Invalid invite code.")

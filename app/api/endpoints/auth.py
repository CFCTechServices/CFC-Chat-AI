from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from app.core.auth import check_invite_code

router = APIRouter(tags=["auth"])

class ValidateInviteRequest(BaseModel):
    invite_code: str

class ValidateInviteResponse(BaseModel):
    valid: bool
    message: str

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
    Validates an invite code using Supabase RPC.
    """
    is_valid = check_invite_code(request.invite_code)
    
    if is_valid:
        return ValidateInviteResponse(valid=True, message="Invite code is valid.")
    else:
        # Return 200 with valid=False or 400? 
        # Frontend logic usually likes 200 OK -> valid=false/true, 
        # but 400 is semantically correct for 'invalid input'. 
        # Given the requirements, we'll return False but keep 200 OK so frontend can handle it gracefully.
        return ValidateInviteResponse(valid=False, message="Invalid invite code.")

from fastapi import APIRouter, HTTPException, Depends
from pydantic import BaseModel
from typing import Optional
import logging
from app.core.auth import get_current_user, get_user_client

logger = logging.getLogger(__name__)
router = APIRouter(tags=["profile"])

# Models
class UpdateProfileRequest(BaseModel):
    full_name: Optional[str] = None
    avatar_url: Optional[str] = None

class ProfileResponse(BaseModel):
    id: str
    email: str
    full_name: Optional[str] = None
    avatar_url: Optional[str] = None
    role: str
    status: str
    created_at: str

@router.get("/me", response_model=ProfileResponse)
async def get_profile(
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Get the current user's profile information.
    Uses user-scoped client that respects RLS.
    """
    try:
        # Query with user's client - RLS ensures user only sees their own profile
        response = user_client.table("profiles")\
            .select("*")\
            .eq("id", current_user.id)\
            .single()\
            .execute()
        
        if not response.data:
            raise HTTPException(status_code=404, detail="Profile not found")
        
        return ProfileResponse(**response.data)
    
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get profile: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to get profile: {str(e)}")

@router.patch("/me", response_model=ProfileResponse)
async def update_profile(
    request: UpdateProfileRequest,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Update the current user's profile.
    Users can update their full_name and avatar_url.
    Uses user-scoped client that respects RLS.
    """
    try:
        # Build update data (only include fields that were provided)
        update_data = {}
        if request.full_name is not None:
            update_data["full_name"] = request.full_name
        if request.avatar_url is not None:
            update_data["avatar_url"] = request.avatar_url
        
        if not update_data:
            raise HTTPException(
                status_code=400,
                detail="No fields to update. Provide full_name or avatar_url."
            )
        
        # Update profile with user's client - RLS ensures user can only update their own
        update_response = user_client.table("profiles")\
            .update(update_data)\
            .eq("id", current_user.id)\
            .execute()
        
        if not update_response.data:
            raise HTTPException(
                status_code=500,
                detail="Failed to update profile"
            )
        
        logger.info(f"User {current_user.id} updated profile: {update_data}")
        
        # Return the updated profile (first item in the array)
        return ProfileResponse(**update_response.data[0])
    
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update profile: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to update profile: {str(e)}")

@router.post("/complete", response_model=ProfileResponse)
async def complete_profile(
    request: UpdateProfileRequest,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Complete user profile after signup.
    This is typically called after authentication to set the user's name.
    Alias for update_profile but with semantic meaning for first-time setup.
    Uses user-scoped client that respects RLS.
    """
    return await update_profile(request, current_user, user_client)

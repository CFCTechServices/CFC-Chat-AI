import os
import logging
from typing import Optional
from fastapi import HTTPException, Header
from supabase import Client, create_client
from dotenv import load_dotenv
from pathlib import Path

logger = logging.getLogger(__name__)

# Load environment variables from .env file
load_dotenv(Path(__file__).parent.parent / ".env")

SUPABASE_URL = os.getenv("SUPABASE_URL")

# Auth service uses SERVICE_ROLE_KEY to verify admin roles and bypass RLS
SUPABASE_SERVICE_ROLE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY")

if not SUPABASE_URL or not SUPABASE_SERVICE_ROLE_KEY:
    raise RuntimeError(
        "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required for authentication. "
        "SERVICE_ROLE_KEY is needed to verify admin roles and bypass RLS."
    )

try:
    # Create Supabase client with SERVICE_ROLE_KEY for admin verification
    supabase: Client = create_client(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY)
except Exception as e:
    raise RuntimeError(f"Failed to initialize Supabase auth client: {e}")

from fastapi import Security
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials

security = HTTPBearer()

def get_current_user_token(credentials: HTTPAuthorizationCredentials = Security(security)) -> str:
    """
    Extract Bearer token from Authorization header.
    """
    return credentials.credentials

async def get_current_user(token: str = Security(get_current_user_token)):
    """
    Verify JWT token with Supabase Auth and return user object.
    Also checks profile status to ensure user is active.
    """
    if not supabase:
        raise HTTPException(status_code=503, detail="Supabase not configured")

    try:
        user_response = supabase.auth.get_user(token)
        if not user_response.user:
            raise HTTPException(status_code=401, detail="Invalid token")
        
        # Check user profile status
        profile_response = supabase.table("profiles")\
            .select("status, deleted_at")\
            .eq("id", user_response.user.id)\
            .single()\
            .execute()
        
        if not profile_response.data:
            raise HTTPException(status_code=403, detail="User profile not found")
        
        status = profile_response.data.get("status")
        if status != "active":
            # Show "inactive" message to match database status
            if status == "inactive":
                raise HTTPException(status_code=403, detail="Account is inactive")
            elif status == "deleted":
                raise HTTPException(status_code=403, detail="Account has been deleted")
            else:
                raise HTTPException(status_code=403, detail="Account is not active")
        
        return user_response.user
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Authentication failed: {e}")
        raise HTTPException(status_code=401, detail="Authentication failed")

def get_user_scoped_client(token: str):
    """
    Create a Supabase client that uses the user's JWT token.
    This client will RESPECT Row Level Security (RLS) policies.
    
    Use this for user-facing operations where RLS should enforce access control.
    
    Args:
        token: The user's JWT token from Authorization header
        
    Returns:
        Supabase client configured with user's token (respects RLS)
    """
    from supabase import create_client
    
    # Create client with ANON_KEY (respects RLS)
    user_client = create_client(SUPABASE_URL, os.getenv("SUPABASE_ANON_KEY"))
    
    # Set the user's session using their JWT token
    # This makes all queries execute with the user's permissions
    user_client.auth.set_session(
        access_token=token,
        refresh_token=""  # Not needed for backend operations
    )
    
    return user_client

async def get_user_client(token: str = Security(get_current_user_token)):
    """
    Dependency that provides a user-scoped Supabase client (respects RLS).
    Use this in endpoints that should respect Row Level Security.
    
    Example:
        @router.get("/profile/me")
        async def get_profile(user_client = Depends(get_user_client)):
            # This query respects RLS - user can only see their own profile
            profile = user_client.table("profiles").select("*").execute()
            return profile.data
    """
    return get_user_scoped_client(token)

async def get_current_admin(user=Security(get_current_user)):
    """
    Verifies that the current user has the 'admin' role.
    """
    if not supabase:
        raise HTTPException(status_code=503, detail="Supabase not configured")

    try:
        # Query profiles table to check role
        response = supabase.table("profiles").select("role").eq("id", user.id).single().execute()
        
        if not response.data:
             raise HTTPException(status_code=403, detail="Profile not found")
             
        role = response.data.get("role")
        if role != "admin":
            raise HTTPException(status_code=403, detail="Insufficient permissions")
            
        return user
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Authorization check failed for user {user.id}: {e}")
        raise HTTPException(status_code=403, detail="Authorization failed")

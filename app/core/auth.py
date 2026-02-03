import os
from typing import Optional
from fastapi import Request, HTTPException, Security
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
from supabase import create_client, Client

# Initialize Supabase Client
SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY") or os.getenv("SUPABASE_ANON_KEY")

if not SUPABASE_URL or not SUPABASE_KEY:
    # Fallback for build/test environments without env vars
    print("Warning: SUPABASE_URL or SUPABASE_KEY not set. Supabase features will fail.")
    supabase: Optional[Client] = None
else:
    supabase: Client = create_client(SUPABASE_URL, SUPABASE_KEY)

security = HTTPBearer()

def get_current_user_token(credentials: HTTPAuthorizationCredentials = Security(security)) -> str:
    """
    Extracts the Bearer token from the Authorization header.
    Validator logic can be expanded here if we want to verify the JWT signature 
    locally, but for now we'll pass it to Supabase or trust it's verified 
    before hitting protected endpoints if we use the Supabase middleware pattern.
    
    However, a common pattern is to just get the user FROM Supabase using the token.
    """
    return credentials.credentials

async def get_current_user(token: str = Security(get_current_user_token)):
    """
    Verifies the token with Supabase Auth and returns the user object.
    """
    if not supabase:
        raise HTTPException(status_code=503, detail="Supabase not configured")

    try:
        # verify the token by getting the user
        user_response = supabase.auth.get_user(token)
        if not user_response.user:
            raise HTTPException(status_code=401, detail="Invalid token")
        return user_response.user
    except Exception as e:
        raise HTTPException(status_code=401, detail=f"Authentication failed: {str(e)}")

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
    except Exception as e:
        if isinstance(e, HTTPException):
            raise e
        raise HTTPException(status_code=403, detail=f"Authorization failed: {str(e)}")

def check_invite_code(invite_code: str) -> bool:
    """
    Calls Supabase RPC to validate an invite code.
    Assumes a postgres function `check_invite_code` exists.
    """
    if not supabase:
        print("Error: Supabase client not initialized.")
        return False
    
    try:
        # The RPC function expects 'lookup_code' as the parameter name based on the error logs.
        response = supabase.rpc("check_invite_code", {"lookup_code": invite_code}).execute()
        # RPC usually returns data directly in response.data
        return bool(response.data)
    except Exception as e:
        print(f"RPC Error: {e}")
        return False
        print(f"RPC Error: {e}")
        # It might be a mismatch in parameter name if we get a 400ish error from PostgREST
        return False

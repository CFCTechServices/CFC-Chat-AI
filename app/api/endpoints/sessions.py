from fastapi import APIRouter, Depends, HTTPException, Security
from pydantic import BaseModel
from typing import List, Optional, Dict, Any
from app.core.auth import get_current_user, get_user_client

router = APIRouter(tags=["sessions"])

# Models
class ChatSession(BaseModel):
    id: str
    user_id:str
    title: Optional[str] = None
    created_at: str

class CreateSessionRequest(BaseModel):
    title: Optional[str] = "New Chat"

class UpdateSessionRequest(BaseModel):
    title: str

class ChatMessage(BaseModel):
    id: str
    session_id: str
    role: str
    content: str
    created_at: str
    metadata: Optional[Dict[str, Any]] = None

# Endpoints

@router.get("/sessions", response_model=List[ChatSession])
async def get_sessions(
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Get all chat sessions for the current user.
    Uses user-scoped client that respects RLS.
    """
    try:
        response = user_client.table("chat_sessions")\
            .select("*")\
            .order("created_at", desc=True)\
            .execute()
        return response.data
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.post("/sessions", response_model=ChatSession)
async def create_session(
    request: CreateSessionRequest,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Create a new chat session.
    Uses user-scoped client - RLS ensures user_id is set correctly.
    """
    try:
        data = {
            "user_id": current_user.id,
            "title": request.title
        }
        response = user_client.table("chat_sessions").insert(data).execute()
        if not response.data:
            raise HTTPException(status_code=500, detail="Failed to create session")
        return response.data[0]
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.get("/sessions/{session_id}", response_model=List[ChatMessage])
async def get_session_history(
    session_id: str,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Get message history for a specific session.
    RLS ensures user can only access their own sessions.
    """
    try:
        # Verify session ownership
        session_check = user_client.table("chat_sessions")\
            .select("id")\
            .eq("id", session_id)\
            .execute()
            
        if not session_check.data:
            raise HTTPException(status_code=404, detail="Session not found")

        # Fetch messages
        messages = user_client.table("chat_messages")\
            .select("*")\
            .eq("session_id", session_id)\
            .order("created_at", desc=False)\
            .execute()
            
        return messages.data
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.patch("/sessions/{session_id}", response_model=ChatSession)
async def update_session_title(
    session_id: str,
    request: UpdateSessionRequest,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Update the title of a chat session.
    RLS ensures users can only update their own sessions.
    """
    try:\
        # Verify session ownership
        session_check = user_client.table("chat_sessions")\
            .select("id")\
            .eq("id", session_id)\
            .execute()
        
        if not session_check.data:
            raise HTTPException(
                status_code=404,
                detail="Session not found or you don't have permission to update it"
            )
        
        # Update session title
        update_response = user_client.table("chat_sessions")\
            .update({"title": request.title})\
            .eq("id", session_id)\
            .execute()
        
        if not update_response.data:
            raise HTTPException(
                status_code=500,
                detail="Failed to update session title"
            )
        
        return update_response.data[0]
    
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

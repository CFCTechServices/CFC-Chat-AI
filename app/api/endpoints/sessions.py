from fastapi import APIRouter, Depends, HTTPException, Security
from pydantic import BaseModel
from typing import List, Optional, Dict, Any
from app.core.auth import get_current_user, supabase

router = APIRouter(tags=["sessions"])

# Models
class ChatSession(BaseModel):
    id: str
    user_id: str
    title: Optional[str] = None
    created_at: str

class CreateSessionRequest(BaseModel):
    title: Optional[str] = "New Chat"

class ChatMessage(BaseModel):
    id: str
    session_id: str
    role: str
    content: str
    created_at: str
    metadata: Optional[Dict[str, Any]] = None

# Endpoints

@router.get("/sessions", response_model=List[ChatSession])
async def get_sessions(user: Any = Depends(get_current_user)):
    """
    Get all chat sessions for the current user.
    """
    try:
        response = supabase.table("chat_sessions")\
            .select("*")\
            .eq("user_id", user.id)\
            .order("created_at", desc=True)\
            .execute()
        return response.data
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.post("/sessions", response_model=ChatSession)
async def create_session(request: CreateSessionRequest, user: Any = Depends(get_current_user)):
    """
    Create a new chat session.
    """
    try:
        data = {
            "user_id": user.id,
            "title": request.title
        }
        response = supabase.table("chat_sessions").insert(data).execute()
        if not response.data:
            raise HTTPException(status_code=500, detail="Failed to create session")
        return response.data[0]
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@router.get("/sessions/{session_id}", response_model=List[ChatMessage])
async def get_session_history(session_id: str, user: Any = Depends(get_current_user)):
    """
    Get full message history for a specific session.
    Verifies that the session belongs to the user.
    """
    try:
        # First verify ownership
        session_check = supabase.table("chat_sessions")\
            .select("id")\
            .eq("id", session_id)\
            .eq("user_id", user.id)\
            .execute()
            
        if not session_check.data:
            raise HTTPException(status_code=404, detail="Session not found")

        # Fetch messages
        messages = supabase.table("chat_messages")\
            .select("*")\
            .eq("session_id", session_id)\
            .order("created_at", desc=False)\
            .execute()
            
        return messages.data
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

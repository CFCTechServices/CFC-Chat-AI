from fastapi import APIRouter, Depends, HTTPException, Security, Query
from pydantic import BaseModel
from typing import List, Optional, Dict, Any
from app.core.auth import get_current_user, get_user_client

router = APIRouter(tags=["sessions"])

# Models
class ChatSession(BaseModel):
    id: str
    user_id: str
    title: Optional[str] = None
    created_at: str

class ChatSessionWithCount(ChatSession):
    message_count: int = 0
    last_message: Optional[str] = None

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

@router.get("/sessions")
async def get_sessions(
    detail: bool = Query(False, description="Include message counts and last message preview"),
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Get all chat sessions for the current user.
    Pass ?detail=true to include message counts and last message preview (used by History page).
    Default lightweight response returns only session metadata (used by Chat sidebar).
    """
    try:
        if not detail:
            # Lightweight query — just session metadata, single DB call
            response = user_client.table("chat_sessions")\
                .select("id, user_id, title, created_at")\
                .order("created_at", desc=True)\
                .execute()
            return response.data

        # Detailed query — includes message counts and last message preview
        response = user_client.table("chat_sessions")\
            .select("*, chat_messages(count)")\
            .order("created_at", desc=True)\
            .execute()

        sessions = []
        for row in response.data:
            msg_count_data = row.pop("chat_messages", [])
            msg_count = msg_count_data[0]["count"] if msg_count_data else 0
            row["message_count"] = msg_count
            row["last_message"] = None

            if msg_count > 0:
                last_msg = user_client.table("chat_messages")\
                    .select("content")\
                    .eq("session_id", row["id"])\
                    .eq("role", "user")\
                    .order("created_at", desc=True)\
                    .limit(1)\
                    .execute()
                if last_msg.data:
                    row["last_message"] = last_msg.data[0]["content"][:100]

            sessions.append(row)

        return sessions
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

@router.delete("/sessions/{session_id}")
async def delete_session(
    session_id: str,
    current_user = Depends(get_current_user),
    user_client = Depends(get_user_client)
):
    """
    Delete a chat session and its messages.
    RLS ensures users can only delete their own sessions.
    """
    try:
        # Verify session ownership
        session_check = user_client.table("chat_sessions")\
            .select("id")\
            .eq("id", session_id)\
            .execute()

        if not session_check.data:
            raise HTTPException(status_code=404, detail="Session not found")

        # Delete messages first (foreign key dependency)
        user_client.table("chat_messages")\
            .delete()\
            .eq("session_id", session_id)\
            .execute()

        # Delete the session
        user_client.table("chat_sessions")\
            .delete()\
            .eq("id", session_id)\
            .execute()

        return {"success": True}
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

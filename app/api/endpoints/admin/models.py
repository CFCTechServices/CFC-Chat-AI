from pydantic import BaseModel, EmailStr
from typing import Optional, List


class InviteRequest(BaseModel):
    email: EmailStr

class InviteResponse(BaseModel):
    message: str
    email: str
    expires_at: str

class InvitationStatusResponse(BaseModel):
    email: str
    status: str  # "active", "expired", "registered", "none"
    expires_at: Optional[str] = None

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

class AdminSettings(BaseModel):
    auto_approve_uploads: bool = False
    email_notifications: bool = True
    maintenance_mode: bool = False

class AdminSettingsUpdate(BaseModel):
    auto_approve_uploads: Optional[bool] = None
    email_notifications: Optional[bool] = None
    maintenance_mode: Optional[bool] = None

class DocumentInfo(BaseModel):
    name: str
    size: int
    status: str  # "ingested" or "pending"

class IngestionStatsResponse(BaseModel):
    total_documents: int
    processing: int
    completed: int
    documents: List[DocumentInfo]

class UserProfile(BaseModel):
    id: str
    email: str
    full_name: Optional[str] = None
    avatar_url: Optional[str] = None
    role: str
    status: str
    created_at: str
    deleted_at: Optional[str] = None
    deleted_by: Optional[str] = None
    last_active: Optional[str] = None  # Placeholder - not tracked in DB yet

class ListUsersResponse(BaseModel):
    users: List[UserProfile]
    total: int

from pydantic import BaseModel, EmailStr
from fastapi import APIRouter, HTTPException
from app.config import settings
from email.message import EmailMessage
import random

router = APIRouter(prefix="/auth", tags=["auth"])
class OTPRequest(BaseModel):
    email: EmailStr

class OTPVerifyRequest(BaseModel):
    email: EmailStr
    otp: str
    
sender = "noreply@cfctech.com"
otp_store = {}

def send_email(to_email: str):
    pass
@router.post("/request-otp")
async def request_otp(request: OTPRequest):
    pass
@router.post("/verify-otp")
async def verify_otp(request: OTPVerifyRequest):
    pass 
@router.post("/resend-otp")
async def resend_otp(request: OTPRequest):
    pass 
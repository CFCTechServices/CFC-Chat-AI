#!/usr/bin/env python3
"""
Test script for admin unban user endpoint (restore banned users)
"""
import requests
import json
import os
from pathlib import Path
from typing import Optional
from dotenv import load_dotenv

# Load environment variables
BASE_DIR = Path(__file__).resolve().parent.parent
load_dotenv(BASE_DIR / ".env")

# ==============================
# CONFIGURATION - UPDATE THESE
# ==============================

# Admin credentials for authentication (loaded from environment)
ADMIN_EMAIL: str = os.getenv("TEST_ADMIN_EMAIL")
ADMIN_PASSWORD: str = os.getenv("TEST_ADMIN_PASSWORD")

# Target user to reactivate (loaded from environment)
TARGET_USER_ID: str = os.getenv("TEST_TARGET_USER_ID")

# API base URL
API_BASE_URL = "http://localhost:8000"

# Supabase credentials (loaded from .env)
SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")

# ==============================
# TEST FUNCTIONS
# ==============================

def authenticate_with_supabase(email: str, password: str) -> Optional[str]:
    """
    Authenticate with Supabase and return JWT token.
    """
    try:
        url = f"{SUPABASE_URL}/auth/v1/token?grant_type=password"
        headers = {
            "apikey": SUPABASE_ANON_KEY,
            "Content-Type": "application/json"
        }
        payload = {
            "email": email,
            "password": password
        }
        
        response = requests.post(url, headers=headers, json=payload)
        
        if response.status_code == 200:
            data = response.json()
            return data.get("access_token")
        else:
            print(f"❌ Authentication failed: {response.status_code}")
            print(f"Response: {response.text}")
            return None
            
    except Exception as e:
        print(f"❌ Error during authentication: {e}")
        return None

def test_reactivate_user(jwt_token: str, user_id: str):
    """
    Test the admin reactivate user endpoint.
    """
    print(f"\n✅ Attempting to reactivate user...")
    print(f"   User ID: {user_id}")
    
    url = f"{API_BASE_URL}/api/admin/users/{user_id}/reactivate"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    
    try:
        response = requests.post(url, headers=headers)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
        except:
            print(response.text)
        
        if response.status_code == 200:
            print(f"\n✅ SUCCESS! User has been reactivated")
            print(f"   🔓 User can now login again")
            print(f"   ✨ Status set back to 'active'")
            print(f"   🗄️  Verify in 'public.profiles' table:")
            print(f"      - status='active'")
            print(f"      - deleted_at=NULL")
            print(f"      - deleted_by=NULL")
        elif response.status_code == 400:
            print(f"\n⚠️  Bad Request: User is not deactivated or doesn't need reactivation")
        elif response.status_code == 403:
            print(f"\n⚠️  Forbidden: User is not an admin")
        elif response.status_code == 404:
            print(f"\n⚠️  Not Found: User ID does not exist")
        else:
            print(f"\n⚠️  Unexpected response code: {response.status_code}")
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")

def main():
    print("=" * 70)
    print("🧪 Admin Reactivate User Endpoint Test Script")
    print("=" * 70)
    
    # Validate configuration
    if not TARGET_USER_ID:
        print("\n❌ ERROR: TARGET_USER_ID is not set!")
        print("Please update the TARGET_USER_ID variable in this script.")
        print("\nTo find a deactivated user ID:")
        print("1. Go to Supabase -> Table Editor -> profiles")
        print("2. Filter: status = 'inactive'")
        print("3. Copy the user's UUID")
        return
    
    if not ADMIN_EMAIL or not ADMIN_PASSWORD:
        print("\n❌ ERROR: Admin credentials not set!")
        print("Please set ADMIN_EMAIL and ADMIN_PASSWORD in this script.")
        return
    
    # Authenticate
    print(f"\n🔐 Authenticating as admin: {ADMIN_EMAIL}...")
    jwt_token = authenticate_with_supabase(ADMIN_EMAIL, ADMIN_PASSWORD)
    
    if not jwt_token:
        print("❌ Failed to obtain JWT token. Exiting.")
        return
    
    print("✅ Authentication successful!")
    
    # Test reactivate user endpoint
    test_reactivate_user(jwt_token, TARGET_USER_ID)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)
    print("\n💡 User should now be able to login again!")

if __name__ == "__main__":
    main()

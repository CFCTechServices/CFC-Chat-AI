#!/usr/bin/env python3
"""
Test script for admin delete user endpoint (hard delete)
WARNING: This permanently deletes users and all their data!
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

# Admin credentials for authentication (loaded from environment, None by default for safety)
ADMIN_EMAIL: str = os.getenv("TEST_ADMIN_EMAIL")
ADMIN_PASSWORD: str = os.getenv("TEST_ADMIN_PASSWORD")

# Target user to PERMANENTLY DELETE (loaded from environment, None by default for safety)
TARGET_USER_ID: str = os.getenv("TEST_TARGET_USER_ID")

# API base URL
API_BASE_URL = os.getenv("API_BASE_URL", "http://localhost:8000")

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

def test_delete_user(jwt_token: str, user_id: str):
    """
    Test the admin delete user endpoint (PERMANENT deletion).
    """
    print(f"\n⚠️  PERMANENT DELETION - THIS CANNOT BE UNDONE!")
    print(f"   User ID: {user_id}")
    print(f"   This will delete:")
    print(f"   - User authentication record")
    print(f"   - User profile")
    print(f"   - All chat sessions and messages")
    print(f"   - (Invitations and feedback will be anonymized)")
    
    confirm = input(f"\n⚠️  Are you sure you want to PERMANENTLY delete this user? (type 'DELETE' to confirm): ")
    
    if confirm != "DELETE":
        print("\n❌ Deletion cancelled.")
        return
    
    url = f"{API_BASE_URL}/api/admin/users/{user_id}"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    
    try:
        response = requests.delete(url, headers=headers)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
        except:
            print(response.text)
        
        if response.status_code == 200:
            print(f"\n✅ SUCCESS! User has been PERMANENTLY deleted")
            print(f"   🗑️  User removed from authentication")
            print(f"   🗑️  Profile deleted from database")
            print(f"   🗑️  All chat sessions and messages deleted (CASCADE)")
            print(f"   ℹ️  Invitations and feedback anonymized (SET NULL)")
            print(f"   ⚠️  This action CANNOT be undone!")
        elif response.status_code == 400:
            print(f"\n⚠️  Bad Request: Check if you're trying to delete yourself")
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
    print("🧪 Admin DELETE User Endpoint Test Script (PERMANENT)")
    print("=" * 70)
    print("\n⚠️  WARNING: This script tests PERMANENT user deletion!")
    print("⚠️  ALL user data will be DESTROYED via CASCADE!")
    print("⚠️  Use the BAN endpoint for reversible soft delete!")
    
    # Validate configuration
    if not TARGET_USER_ID:
        print("\n❌ ERROR: TARGET_USER_ID is not set!")
        print("Please update the TARGET_USER_ID variable in this script.")
        print("\nTo find a user ID:")
        print("1. Go to Supabase -> Authentication -> Users")
        print("2. Click on a user to see their UUID")
        print("3. Copy the UUID and paste it into TARGET_USER_ID")
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
    
    # Test delete user endpoint
    test_delete_user(jwt_token, TARGET_USER_ID)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)

if __name__ == "__main__":
    main()

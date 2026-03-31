#!/usr/bin/env python3
"""
Test script for admin change-role endpoint
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

# Target user whose role you want to change (loaded from environment)
TARGET_USER_ID: str = os.getenv("TEST_TARGET_USER_ID")
NEW_ROLE: str = "dev"  # TODO: One of: 'user', 'dev', 'admin'

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

def test_change_role(jwt_token: str, user_id: str, new_role: str):
    """
    Test the admin change-role endpoint.
    """
    print(f"\n🔄 Attempting to change user role...")
    print(f"   User ID: {user_id}")
    print(f"   New Role: {new_role}")
    
    url = f"{API_BASE_URL}/api/admin/change-role"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "user_id": user_id,
        "new_role": new_role
    }
    
    try:
        response = requests.post(url, headers=headers, json=payload)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
        except:
            print(response.text)
        
        if response.status_code == 200:
            print(f"\n✅ SUCCESS! User role changed to '{new_role}'")
            print(f"   You can verify this in the 'public.profiles' table in your database.")
        elif response.status_code == 400:
            print(f"\n⚠️  Bad Request: Invalid role or parameters")
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
    print("🧪 Admin Change Role Endpoint Test Script")
    print("=" * 70)
    
    # Validate configuration
    if not TARGET_USER_ID:
        print("\n❌ ERROR: TARGET_USER_ID is not set!")
        print("Please update the TARGET_USER_ID variable in this script.")
        print("\nTo find a user ID:")
        print("1. Go to Supabase -> Authentication -> Users")
        print("2. Click on a user to see their UUID")
        print("3. Copy the UUID and paste it into TARGET_USER_ID")
        return
    
    if NEW_ROLE not in ['user', 'dev', 'admin']:
        print(f"\n❌ ERROR: Invalid role '{NEW_ROLE}'")
        print("NEW_ROLE must be one of: 'user', 'dev', 'admin'")
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
    
    # Test change role endpoint
    test_change_role(jwt_token, TARGET_USER_ID, NEW_ROLE)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)

if __name__ == "__main__":
    main()

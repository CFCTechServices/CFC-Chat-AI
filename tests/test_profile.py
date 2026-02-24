#!/usr/bin/env python3
"""
Test script for profile endpoints (get and update name)
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

# User credentials
# USER_EMAIL: str = None  # TODO: Set your email here
# USER_PASSWORD: str = None  # TODO: Set your password here

USER_EMAIL: str = os.getenv("TEST_ADMIN_EMAIL")
USER_PASSWORD: str = os.getenv("TEST_ADMIN_PASSWORD")

# New name to set
NEW_NAME: str = "Shash"  # TODO: Set the name you want to use

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

def test_get_profile(jwt_token: str):
    """
    Test getting current user profile.
    """
    print(f"\n📋 Getting current profile...")
    
    url = f"{API_BASE_URL}/api/profile/me"
    headers = {
        "Authorization": f"Bearer {jwt_token}"
    }
    
    try:
        response = requests.get(url, headers=headers)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
            return response_json
        except:
            print(response.text)
            return None
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")
        return None

def test_update_profile(jwt_token: str, full_name: str):
    """
    Test updating user profile name.
    """
    print(f"\n✏️  Updating profile name to: {full_name}")
    
    url = f"{API_BASE_URL}/api/profile/me"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "full_name": full_name
    }
    
    try:
        response = requests.patch(url, headers=headers, json=payload)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
            
            if response.status_code == 200:
                print(f"\n✅ SUCCESS! Profile updated")
                print(f"   👤 New name: {response_json.get('full_name')}")
            
            return response_json
        except:
            print(response.text)
            return None
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")
        return None

def test_complete_profile(jwt_token: str, full_name: str):
    """
    Test completing profile (first-time name setup).
    """
    print(f"\n🚀 Completing profile with name: {full_name}")
    
    url = f"{API_BASE_URL}/api/profile/complete"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "full_name": full_name
    }
    
    try:
        response = requests.post(url, headers=headers, json=payload)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
            
            if response.status_code == 200:
                print(f"\n✅ SUCCESS! Profile completed")
                print(f"   👤 Name: {response_json.get('full_name')}")
            
            return response_json
        except:
            print(response.text)
            return None
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")
        return None

def main():
    print("=" * 70)
    print("🧪 Profile Management Endpoints Test Script")
    print("=" * 70)
    
    # Validate configuration
    if not USER_EMAIL or not USER_PASSWORD:
        print("\n❌ ERROR: User credentials not set!")
        print("Please set USER_EMAIL and USER_PASSWORD in this script.")
        return
    
    # Authenticate
    print(f"\n🔐 Authenticating as: {USER_EMAIL}...")
    jwt_token = authenticate_with_supabase(USER_EMAIL, USER_PASSWORD)
    
    if not jwt_token:
        print("❌ Failed to obtain JWT token. Exiting.")
        return
    
    print("✅ Authentication successful!")
    
    # Test 1: Get current profile
    print("\n" + "=" * 70)
    print("TEST 1: Get Current Profile")
    print("=" * 70)
    current_profile = test_get_profile(jwt_token)
    
    # Test 2: Update profile name
    print("\n" + "=" * 70)
    print("TEST 2: Update Profile Name")
    print("=" * 70)
    test_update_profile(jwt_token, NEW_NAME)
    
    # Test 3: Get updated profile
    print("\n" + "=" * 70)
    print("TEST 3: Verify Updated Profile")
    print("=" * 70)
    test_get_profile(jwt_token)
    
    # Optional: Test complete profile endpoint
    print("\n" + "=" * 70)
    print("TEST 4: Complete Profile (Alternative Endpoint)")
    print("=" * 70)
    test_complete_profile(jwt_token, NEW_NAME)
    
    print("\n" + "=" * 70)
    print("🏁 All Tests Complete")
    print("=" * 70)

if __name__ == "__main__":
    main()

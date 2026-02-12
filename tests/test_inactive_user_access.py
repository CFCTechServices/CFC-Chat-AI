#!/usr/bin/env python3
"""
Test script to verify that deactivated users cannot access the API
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

# User credentials (this should be a user who is deactivated)
DEACTIVATED_USER_EMAIL: str = os.getenv("TEST_DEACTIVATED_USER_EMAIL", "ogxpsych@gmail.com")
DEACTIVATED_USER_PASSWORD: str = os.getenv("TEST_DEACTIVATED_USER_PASSWORD", "12345678")

# API base URL
API_BASE_URL = "http://localhost:8000"

# Supabase credentials
SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")

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

def test_api_access(jwt_token: str):
    """
    Test accessing a protected API endpoint.
    """
    print(f"\n🔒 Attempting to access protected endpoint...")
    
    # Try to access the profile endpoint
    url = f"{API_BASE_URL}/api/profile/me"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    
    try:
        response = requests.get(url, headers=headers)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
        except:
            print(response.text)
        
        if response.status_code == 403:
            print(f"\n✅ SUCCESS! Deactivated user was blocked from accessing the API")
            print(f"   The authentication check is working correctly.")
            return True
        elif response.status_code == 200:
            print(f"\n❌ FAILURE! Deactivated user was able to access the API")
            print(f"   This is a security issue - inactive users should not have access.")
            return False
        else:
            print(f"\n⚠️  Unexpected response code: {response.status_code}")
            return False
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")
        return False

def main():
    print("=" * 70)
    print("🧪 Deactivated User Access Test Script")
    print("=" * 70)
    print("\nThis test verifies that deactivated users cannot access the API.")
    print(f"Testing with user: {DEACTIVATED_USER_EMAIL}")
    
    # Authenticate
    print(f"\n🔐 Authenticating (getting JWT token)...")
    jwt_token = authenticate_with_supabase(DEACTIVATED_USER_EMAIL, DEACTIVATED_USER_PASSWORD)
    
    if not jwt_token:
        print("❌ Failed to obtain JWT token.")
        print("⚠️  Note: User can still get a token from Supabase Auth,")
        print("   but they should be blocked when trying to use the API.")
        return
    
    print("✅ JWT token obtained successfully!")
    print("   (Supabase Auth still issues tokens for deactivated users)")
    
    # Test API access
    success = test_api_access(jwt_token)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)
    
    if success:
        print("\n✅ Security check passed: Deactivated users are blocked!")
    else:
        print("\n❌ Security check failed: Deactivated users can still access the API!")

if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
Test script for the admin invite endpoint.
Fill in the configuration section below with your admin credentials.
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

# ============================================================================
# CONFIGURATION - Fill in your details here
# ============================================================================

# Option 1: If you already have an admin JWT token, paste it here
ADMIN_JWT_TOKEN: Optional[str] = None  # Set to None to use email/password below

# Option 2: If you need to authenticate, provide admin credentials (loaded from environment)
ADMIN_EMAIL: Optional[str] = os.getenv("TEST_ADMIN_EMAIL")
ADMIN_PASSWORD: Optional[str] = os.getenv("TEST_ADMIN_PASSWORD")

# The email address to send the invitation to
TARGET_EMAIL = "ogxpsych@gmail.com"  # TODO: Set target email here

# API base URL
API_BASE_URL = os.getenv("API_BASE_URL", "http://localhost:8000")

# Supabase configuration (loaded from .env)
SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")

# ============================================================================
# Script Logic - No need to modify below this line
# ============================================================================

def get_jwt_token_from_supabase(email: str, password: str) -> Optional[str]:
    """
    Authenticates with Supabase and returns a JWT token.
    """
    print(f"🔐 Authenticating with Supabase as {email}...")
    
    url = f"{SUPABASE_URL}/auth/v1/token?grant_type=password"
    headers = {
        "apikey": SUPABASE_ANON_KEY,
        "Content-Type": "application/json"
    }
    payload = {
        "email": email,
        "password": password
    }
    
    try:
        response = requests.post(url, headers=headers, json=payload)
        response.raise_for_status()
        data = response.json()
        token = data.get("access_token")
        
        if token:
            print("✅ Authentication successful!")
            return token
        else:
            print("❌ No access token in response")
            return None
            
    except requests.exceptions.RequestException as e:
        print(f"❌ Authentication failed: {e}")
        if hasattr(e, 'response') and e.response is not None:
            print(f"Response: {e.response.text}")
        return None


def test_admin_invite(jwt_token: str, target_email: str):
    """
    Tests the admin invite endpoint.
    """
    print(f"\n📧 Sending invitation to {target_email}...")
    
    url = f"{API_BASE_URL}/api/admin/invite"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "email": target_email
    }
    
    try:
        response = requests.post(url, headers=headers, json=payload)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        print(json.dumps(response.json(), indent=2))
        
        if response.status_code == 200:
            response_data = response.json()
            message = response_data.get("message", "")
            
            # Check if email was actually sent or just created
            if "email disabled" in message.lower():
                print("\n✅ SUCCESS! Invitation created in database!")
                print(f"   ℹ️  Email sending is currently disabled.")
                print(f"   📋 The invitation code has been stored in the database.")
                print(f"   📧 Share the invite code manually with {target_email}")
            else:
                print("\n✅ SUCCESS! Invitation sent successfully!")
                print(f"   📧 Email sent to {target_email}")
                print(f"   📬 Check the inbox for the invitation email.")
            
            print(f"   🗄️  Verify in 'public.invitations' table in your database.")
        elif response.status_code == 403:
            print("\n❌ FORBIDDEN: User does not have admin permissions")
        elif response.status_code == 401:
            print("\n❌ UNAUTHORIZED: Invalid or expired JWT token")
        else:
            print(f"\n⚠️  Unexpected response code: {response.status_code}")
            
    except requests.exceptions.RequestException as e:
        print(f"❌ Request failed: {e}")
        if hasattr(e, 'response') and e.response is not None:
            print(f"Response: {e.response.text}")


def main():
    """
    Main execution function.
    """
    print("=" * 70)
    print("🧪 Admin Invite Endpoint Test Script")
    print("=" * 70)
    
    jwt_token = None
    
    # Determine which authentication method to use
    if ADMIN_JWT_TOKEN:
        print("\n✓ Using provided JWT token")
        jwt_token = ADMIN_JWT_TOKEN
    elif ADMIN_EMAIL and ADMIN_PASSWORD:
        print("\n✓ Using email/password authentication")
        jwt_token = get_jwt_token_from_supabase(ADMIN_EMAIL, ADMIN_PASSWORD)
    else:
        print("\n❌ ERROR: No authentication method configured!")
        print("\nPlease edit this script and provide either:")
        print("  1. ADMIN_JWT_TOKEN (if you already have a token)")
        print("  2. ADMIN_EMAIL and ADMIN_PASSWORD (to authenticate)")
        return
    
    if not jwt_token:
        print("\n❌ Failed to obtain JWT token. Exiting.")
        return
    
    if not TARGET_EMAIL:
        print("\n❌ ERROR: TARGET_EMAIL is not set!")
        print("Please edit this script and set the TARGET_EMAIL variable.")
        return
    
    # Test the admin invite endpoint
    test_admin_invite(jwt_token, TARGET_EMAIL)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)


if __name__ == "__main__":
    main()

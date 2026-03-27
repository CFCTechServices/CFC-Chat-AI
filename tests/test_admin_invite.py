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
API_BASE_URL = "http://localhost:8000"

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


import pytest

pytestmark = pytest.mark.integration


def test_admin_invite(real_jwt_token: str, target_email: str):
    """Test the admin invite endpoint."""
    url = f"{API_BASE_URL}/api/admin/invite"
    headers = {
        "Authorization": f"Bearer {real_jwt_token}",
        "Content-Type": "application/json",
    }
    payload = {"email": target_email}

    response = requests.post(url, headers=headers, json=payload)
    assert response.status_code == 200, (
        f"Expected 200 but got {response.status_code}: {response.text}"
    )
    data = response.json()
    assert "message" in data, f"Unexpected response body: {data}"
    assert "email" in data, f"Response missing 'email': {data}"
    assert "expires_at" in data, f"Response missing 'expires_at': {data}"


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

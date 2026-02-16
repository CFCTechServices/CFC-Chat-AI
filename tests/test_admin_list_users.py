#!/usr/bin/env python3
"""
Test script for the admin list users endpoint.
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


def test_list_users(jwt_token: str):
    """
    Tests the admin list users endpoint.
    """
    print(f"\n👥 Fetching list of all users...")
    
    url = f"{API_BASE_URL}/api/admin/users"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    
    try:
        response = requests.get(url, headers=headers)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        response_data = response.json()
        print(json.dumps(response_data, indent=2))
        
        if response.status_code == 200:
            users = response_data.get("users", [])
            total = response_data.get("total", 0)
            
            print(f"\n✅ SUCCESS! Retrieved {total} user(s)")
            
            if users:
                print(f"\n📋 User Summary:")
                print("-" * 70)
                for i, user in enumerate(users, 1):
                    status_emoji = {
                        'active': '✓',
                        'inactive': '⊗',
                        'deleted': '✗'
                    }.get(user.get('status'), '?')
                    
                    role_badge = user.get('role', 'unknown').upper()
                    status_badge = user.get('status', 'unknown').upper()
                    
                    print(f"{i}. {status_emoji} {user.get('email', 'N/A')}")
                    print(f"   Name: {user.get('full_name') or '(not set)'}")
                    print(f"   Role: {role_badge} | Status: {status_badge}")
                    print(f"   Created: {user.get('created_at', 'N/A')}")
                    
                    if user.get('deleted_at'):
                        print(f"   Deactivated: {user.get('deleted_at')}")
                    
                    print()
                
                print("-" * 70)
                
                # Verify sorting
                print("\n🔍 Verification:")
                active_count = sum(1 for u in users if u.get('status') == 'active')
                inactive_count = sum(1 for u in users if u.get('status') == 'inactive')
                deleted_count = sum(1 for u in users if u.get('status') == 'deleted')
                
                print(f"   Active users: {active_count}")
                print(f"   Inactive users: {inactive_count}")
                print(f"   Deleted users: {deleted_count}")
                
                # Check if active users appear first
                first_active_idx = next((i for i, u in enumerate(users) if u.get('status') == 'active'), None)
                first_inactive_idx = next((i for i, u in enumerate(users) if u.get('status') in ['inactive', 'deleted']), None)
                
                if first_active_idx is not None and first_inactive_idx is not None:
                    if first_active_idx < first_inactive_idx:
                        print("   ✅ Sorting verified: Active users appear first")
                    else:
                        print("   ⚠️  Warning: Inactive users appear before active users")
                elif first_active_idx is not None:
                    print("   ✅ All users are active")
                
            else:
                print("\n   ℹ️  No users found in database")
                
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
    print("🧪 Admin List Users Endpoint Test Script")
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
    
    # Test the admin list users endpoint
    test_list_users(jwt_token)
    
    print("\n" + "=" * 70)
    print("🏁 Test Complete")
    print("=" * 70)


if __name__ == "__main__":
    main()

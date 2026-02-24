#!/usr/bin/env python3
"""
Test script for updating chat session titles
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
USER_EMAIL: str = None  # TODO: Set your email here
USER_PASSWORD: str = None  # TODO: Set your password here

# Session to update (leave None to create a new one)
SESSION_ID: Optional[str] = None  # TODO: Set session ID or leave None

# New title for the session
NEW_TITLE: str = "My Updated Chat Session"  # TODO: Set the new title

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

def get_sessions(jwt_token: str):
    """
    Get all chat sessions for the user.
    """
    print(f"\n📋 Getting all chat sessions...")
    
    url = f"{API_BASE_URL}/api/chat/sessions"
    headers = {
        "Authorization": f"Bearer {jwt_token}"
    }
    
    try:
        response = requests.get(url, headers=headers)
        
        if response.status_code == 200:
            sessions = response.json()
            print(f"\n✅ Found {len(sessions)} session(s)")
            for session in sessions:
                print(f"   - {session['id']}: {session.get('title', 'No title')}")
            return sessions
        else:
            print(f"❌ Failed to get sessions: {response.status_code}")
            return []
            
    except Exception as e:
        print(f"\n❌ Error: {e}")
        return []

def create_session(jwt_token: str, title: str):
    """
    Create a new chat session.
    """
    print(f"\n🆕 Creating new chat session with title: {title}")
    
    url = f"{API_BASE_URL}/api/chat/sessions"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "title": title
    }
    
    try:
        response = requests.post(url, headers=headers, json=payload)
        
        if response.status_code == 200:
            session = response.json()
            print(f"\n✅ Created session: {session['id']}")
            print(f"   Title: {session['title']}")
            return session['id']
        else:
            print(f"❌ Failed to create session: {response.status_code}")
            return None
            
    except Exception as e:
        print(f"\n❌ Error: {e}")
        return None

def update_session_title(jwt_token: str, session_id: str, new_title: str):
    """
    Update the title of a chat session.
    """
    print(f"\n✏️  Updating session title...")
    print(f"   Session ID: {session_id}")
    print(f"   New Title: {new_title}")
    
    url = f"{API_BASE_URL}/api/chat/sessions/{session_id}"
    headers = {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json"
    }
    payload = {
        "title": new_title
    }
    
    try:
        response = requests.patch(url, headers=headers, json=payload)
        
        print(f"\n📊 Response Status: {response.status_code}")
        print(f"📝 Response Body:")
        try:
            response_json = response.json()
            print(json.dumps(response_json, indent=2))
            
            if response.status_code == 200:
                print(f"\n✅ SUCCESS! Session title updated")
                print(f"   📝 New title: {response_json.get('title')}")
                print(f"   🆔 Session ID: {response_json.get('id')}")
            elif response.status_code == 404:
                print(f"\n⚠️  Session not found or you don't have permission")
            
            return response_json
        except:
            print(response.text)
            return None
            
    except Exception as e:
        print(f"\n❌ Error calling endpoint: {e}")
        return None

def main():
    print("=" * 70)
    print("🧪 Chat Session Title Update Test Script")
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
    
    # Get existing sessions
    print("\n" + "=" * 70)
    print("TEST 1: Get Existing Sessions")
    print("=" * 70)
    sessions = get_sessions(jwt_token)
    
    # Determine which session to update
    target_session_id = SESSION_ID
    
    if not target_session_id:
        if sessions:
            # Use the first session
            target_session_id = sessions[0]['id']
            print(f"\n💡 Using first session: {target_session_id}")
        else:
            # Create a new session
            print("\n💡 No sessions found. Creating a new one...")
            target_session_id = create_session(jwt_token, "Test Session")
    
    if not target_session_id:
        print("\n❌ No session available to update. Exiting.")
        return
    
    # Update session title
    print("\n" + "=" * 70)
    print("TEST 2: Update Session Title")
    print("=" * 70)
    update_session_title(jwt_token, target_session_id, NEW_TITLE)
    
    # Verify update
    print("\n" + "=" * 70)
    print("TEST 3: Verify Update")
    print("=" * 70)
    get_sessions(jwt_token)
    
    print("\n" + "=" * 70)
    print("🏁 All Tests Complete")
    print("=" * 70)

if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
Integration tests for the admin invitation (whitelist) endpoint — all scenarios.

Covers:
  1. Create a new invitation (happy path) — verify email & expires_at in response
  2. Duplicate invite for same email (active) — expect 409
  3. GET /invitations/status/{email} — active state
  4. GET /invitations/status/{email} — "none" for unknown email
  5. Expire an existing invite via direct DB update, then re-invite — expect success
  6. GET /invitations/status/{email} — expired state
  7. Mark invite as registered via DB, then GET status — "registered" state
  8. Unauthenticated access — expect 401/403

Prerequisites:
  - The FastAPI server must be running at API_BASE_URL
  - .env must contain TEST_ADMIN_EMAIL, TEST_ADMIN_PASSWORD, SUPABASE_URL,
    SUPABASE_ANON_KEY, and SUPABASE_SERVICE_ROLE_KEY
"""

import requests
import json
import os
import sys
from pathlib import Path
from typing import Optional
from dotenv import load_dotenv
from supabase import create_client

# Load environment variables
BASE_DIR = Path(__file__).resolve().parent.parent
load_dotenv(BASE_DIR / ".env")

# ============================================================================
# CONFIGURATION
# ============================================================================

ADMIN_EMAIL: Optional[str] = os.getenv("TEST_ADMIN_EMAIL")
ADMIN_PASSWORD: Optional[str] = os.getenv("TEST_ADMIN_PASSWORD")

# Use a unique test email so we don't collide with real invitations
TARGET_EMAIL = os.getenv("TEST_INVITE_TARGET_EMAIL", "test-invite-flow@example.com")

API_BASE_URL = "http://localhost:8000"

SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")
SUPABASE_SERVICE_ROLE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY")

# ============================================================================
# HELPERS
# ============================================================================

# Admin Supabase client for direct DB manipulation in tests
_sb = None

def get_supabase_admin_client():
    global _sb
    if _sb is None:
        _sb = create_client(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY)
    return _sb


def authenticate(email: str, password: str) -> Optional[str]:
    """Authenticate with Supabase and return a JWT access token."""
    print(f"  Authenticating as {email}...")
    url = f"{SUPABASE_URL}/auth/v1/token?grant_type=password"
    headers = {"apikey": SUPABASE_ANON_KEY, "Content-Type": "application/json"}
    payload = {"email": email, "password": password}

    try:
        resp = requests.post(url, headers=headers, json=payload)
        resp.raise_for_status()
        token = resp.json().get("access_token")
        if token:
            print("  Authentication successful!")
            return token
        print("  No access_token in response")
        return None
    except requests.exceptions.RequestException as e:
        print(f"  Authentication failed: {e}")
        return None


def api_headers(jwt_token: str) -> dict:
    return {
        "Authorization": f"Bearer {jwt_token}",
        "Content-Type": "application/json",
    }


def cleanup_test_invitations(email: str):
    """Remove all invitations for the test email so tests start fresh."""
    sb = get_supabase_admin_client()
    sb.table("invitations").delete().eq("email", email).execute()
    print(f"  Cleaned up invitations for {email}")


def expire_invitation(invitation_id: str):
    """Set expires_at to the past to simulate an expired invite."""
    sb = get_supabase_admin_client()
    sb.table("invitations")\
        .update({"expires_at": "2020-01-01T00:00:00+00:00"})\
        .eq("id", invitation_id)\
        .execute()
    print(f"  Expired invitation {invitation_id}")


def mark_invitation_registered(invitation_id: str):
    """Mark an invitation as registered via direct DB update."""
    sb = get_supabase_admin_client()
    sb.table("invitations")\
        .update({"is_registered": True})\
        .eq("id", invitation_id)\
        .execute()
    print(f"  Marked invitation {invitation_id} as registered")


# ============================================================================
# TEST FUNCTIONS
# ============================================================================

import pytest

pytestmark = pytest.mark.integration


@pytest.fixture(autouse=True, scope="module")
def clean_test_invitations(email):
    """Remove all invitations for the test email before and after the suite."""
    cleanup_test_invitations(email)
    yield
    cleanup_test_invitations(email)


def test_create_invite_success(real_jwt_token: str, email: str):
    """Test 1: Create a brand-new invitation — expect 200 with email & expires_at."""
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(real_jwt_token), json={"email": email})
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {data}"
    assert "expires_at" in data, f"Response missing 'expires_at': {data}"
    assert data.get("email") == email, f"Email mismatch: {data}"


def test_duplicate_invite_rejected(real_jwt_token: str, email: str):
    """Test 2: Creating a second invite for the same email while active — expect 409."""
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(real_jwt_token), json={"email": email})
    data = resp.json()

    assert resp.status_code == 409, f"Expected 409, got {resp.status_code}: {data}"
    assert "pending invitation" in data.get("detail", "").lower(), (
        f"Unexpected error detail: {data}"
    )


def test_status_active(real_jwt_token: str, email: str):
    """Test 3: GET /invitations/status/{email} returns 'active'."""
    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(real_jwt_token))
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {data}"
    assert data.get("status") == "active", f"Expected status 'active', got: {data}"
    assert data.get("expires_at") is not None, f"Response missing 'expires_at': {data}"


def test_status_none(real_jwt_token: str):
    """Test 4: GET /invitations/status/{email} for unknown email returns 'none'."""
    unknown = "nonexistent-test-user@example.com"
    url = f"{API_BASE_URL}/api/admin/invitations/status/{unknown}"
    resp = requests.get(url, headers=api_headers(real_jwt_token))
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {data}"
    assert data.get("status") == "none", f"Expected status 'none', got: {data}"


def test_expired_invite_replaced(real_jwt_token: str, email: str):
    """Test 5: After expiring the current invite, a new invite should succeed."""
    sb = get_supabase_admin_client()
    current = sb.table("invitations").select("id").eq("email", email).eq("is_registered", False).execute()
    assert current.data, "No active invite found to expire — did test_create_invite_success run first?"

    old_id = current.data[0]["id"]
    expire_invitation(old_id)

    # Backend deletes expired invites before creating a new one
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(real_jwt_token), json={"email": email})
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200 after expiry, got {resp.status_code}: {data}"
    assert "expires_at" in data, f"Unexpected response body: {data}"

    # Old invite should have been deleted by the backend
    old_row = sb.table("invitations").select("id").eq("id", old_id).execute()
    assert not old_row.data, f"Expired invite was not deleted by the backend: {old_row.data}"


def test_status_expired(real_jwt_token: str, email: str):
    """Test 6: Expire the current invite via DB, then check status returns 'expired'."""
    sb = get_supabase_admin_client()
    current = sb.table("invitations").select("id").eq("email", email).eq("is_registered", False).execute()
    assert current.data, "No active invite found to expire"

    expire_invitation(current.data[0]["id"])

    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(real_jwt_token))
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {data}"
    assert data.get("status") == "expired", f"Expected status 'expired', got: {data}"


def test_status_registered(real_jwt_token: str, email: str):
    """Test 7: Mark invite as registered, then check status returns 'registered'."""
    # Create a fresh invite (current one is expired from test 6)
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(real_jwt_token), json={"email": email})
    assert resp.status_code == 200, f"Setup failed — could not create fresh invite: {resp.text}"

    sb = get_supabase_admin_client()
    current = sb.table("invitations").select("id").eq("email", email).eq("is_registered", False).execute()
    assert current.data, "No active invite found to mark as registered"

    mark_invitation_registered(current.data[0]["id"])

    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(real_jwt_token))
    data = resp.json()

    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {data}"
    assert data.get("status") == "registered", f"Expected status 'registered', got: {data}"


def test_unauthenticated_access():
    """Test 8: Requests without a valid token are rejected."""
    resp1 = requests.post(
        f"{API_BASE_URL}/api/admin/invite",
        headers={"Content-Type": "application/json"},
        json={"email": "anyone@example.com"},
    )
    assert resp1.status_code in (401, 403), (
        f"Expected 401/403 for unauthenticated POST /invite, got {resp1.status_code}"
    )

    resp2 = requests.get(
        f"{API_BASE_URL}/api/admin/invitations/status/anyone@example.com",
        headers={"Content-Type": "application/json"},
    )
    assert resp2.status_code in (401, 403), (
        f"Expected 401/403 for unauthenticated GET /invitations/status, got {resp2.status_code}"
    )


# ============================================================================
# MAIN
# ============================================================================

def main():
    global passed, failed

    print("=" * 70)
    print("  Admin Invitation Flows — Test Suite")
    print("=" * 70)

    # Pre-flight checks
    missing = []
    if not ADMIN_EMAIL:
        missing.append("TEST_ADMIN_EMAIL")
    if not ADMIN_PASSWORD:
        missing.append("TEST_ADMIN_PASSWORD")
    if not SUPABASE_URL:
        missing.append("SUPABASE_URL")
    if not SUPABASE_ANON_KEY:
        missing.append("SUPABASE_ANON_KEY")
    if not SUPABASE_SERVICE_ROLE_KEY:
        missing.append("SUPABASE_SERVICE_ROLE_KEY")

    if missing:
        print(f"\n  ERROR: Missing env vars: {', '.join(missing)}")
        print("  Set them in .env or export before running.")
        sys.exit(1)

    # Authenticate
    jwt_token = authenticate(ADMIN_EMAIL, ADMIN_PASSWORD)
    if not jwt_token:
        print("  Failed to obtain JWT token. Exiting.")
        sys.exit(1)

    # Clean slate
    print(f"\n  Cleaning up previous test invitations for {TARGET_EMAIL}...")
    cleanup_test_invitations(TARGET_EMAIL)

    # Run tests in order (some depend on prior state)
    test_create_invite_success(jwt_token, TARGET_EMAIL)        # 1
    test_duplicate_invite_rejected(jwt_token, TARGET_EMAIL)    # 2
    test_status_active(jwt_token, TARGET_EMAIL)                # 3
    test_status_none(jwt_token)                                # 4
    test_expired_invite_replaced(jwt_token, TARGET_EMAIL)      # 5
    test_status_expired(jwt_token, TARGET_EMAIL)               # 6
    test_status_registered(jwt_token, TARGET_EMAIL)            # 7
    test_unauthenticated_access()                              # 8

    # Final cleanup
    print(f"\n  Final cleanup for {TARGET_EMAIL}...")
    cleanup_test_invitations(TARGET_EMAIL)

    # Summary
    total = passed + failed
    print("\n" + "=" * 70)
    print(f"  Results: {passed}/{total} passed, {failed}/{total} failed")
    print("=" * 70)
    sys.exit(0 if failed == 0 else 1)


if __name__ == "__main__":
    main()

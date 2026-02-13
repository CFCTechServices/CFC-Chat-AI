#!/usr/bin/env python3
"""
Test script for the admin invitation endpoint — all scenarios.

Covers:
  1. Create a new invitation (happy path) — verify code & expires_at in response
  2. Duplicate invite for same email (active) — expect 409
  3. GET /invitations/status/{email} — active state
  4. GET /invitations/status/{email} — "none" for unknown email
  5. Expire an existing invite via direct DB update, then re-invite — expect success
  6. GET /invitations/status/{email} — expired state
  7. Mark invite as used via DB, then GET status — "used" state
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
    """Set expires_at to the past so the invite is expired but still is_used=false."""
    sb = get_supabase_admin_client()
    sb.table("invitations")\
        .update({"expires_at": "2020-01-01T00:00:00+00:00"})\
        .eq("id", invitation_id)\
        .execute()
    print(f"  Expired invitation {invitation_id}")


def mark_invitation_used(invitation_id: str):
    """Mark an invitation as used via direct DB update."""
    sb = get_supabase_admin_client()
    sb.table("invitations")\
        .update({"is_used": True})\
        .eq("id", invitation_id)\
        .execute()
    print(f"  Marked invitation {invitation_id} as used")


# ============================================================================
# TEST FUNCTIONS
# ============================================================================

passed = 0
failed = 0


def report(test_name: str, success: bool, detail: str = ""):
    global passed, failed
    if success:
        passed += 1
        print(f"  PASS: {test_name}")
    else:
        failed += 1
        print(f"  FAIL: {test_name}")
    if detail:
        print(f"        {detail}")


def test_create_invite_success(jwt_token: str, email: str) -> Optional[dict]:
    """Test 1: Create a brand-new invitation — expect 200 with code & expires_at."""
    print("\n--- Test 1: Create new invitation (happy path) ---")
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(jwt_token), json={"email": email})
    data = resp.json()

    ok = (
        resp.status_code == 200
        and "code" in data
        and "expires_at" in data
        and data.get("email") == email
    )
    report(
        "Create invitation returns 200 with code & expires_at",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )
    return data if ok else None


def test_duplicate_invite_rejected(jwt_token: str, email: str):
    """Test 2: Creating a second invite for the same email while active — expect 409."""
    print("\n--- Test 2: Duplicate active invite rejected (409) ---")
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(jwt_token), json={"email": email})
    data = resp.json()

    ok = resp.status_code == 409 and "active invitation already exists" in data.get("detail", "").lower()
    report(
        "Duplicate active invite returns 409",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )


def test_status_active(jwt_token: str, email: str):
    """Test 3: GET /invitations/status/{email} returns 'active'."""
    print("\n--- Test 3: Invitation status — active ---")
    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(jwt_token))
    data = resp.json()

    ok = (
        resp.status_code == 200
        and data.get("status") == "active"
        and data.get("code") is not None
        and data.get("expires_at") is not None
    )
    report(
        "Status endpoint returns 'active' for live invite",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )


def test_status_none(jwt_token: str):
    """Test 4: GET /invitations/status/{email} for unknown email returns 'none'."""
    print("\n--- Test 4: Invitation status — none ---")
    unknown = "nonexistent-test-user@example.com"
    url = f"{API_BASE_URL}/api/admin/invitations/status/{unknown}"
    resp = requests.get(url, headers=api_headers(jwt_token))
    data = resp.json()

    ok = resp.status_code == 200 and data.get("status") == "none"
    report(
        "Status endpoint returns 'none' for unknown email",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )


def test_expired_invite_replaced(jwt_token: str, email: str) -> Optional[dict]:
    """Test 5: After expiring the current invite, a new invite should succeed."""
    print("\n--- Test 5: Expired invite gets replaced with new invite ---")

    # First, fetch the current invitation id from DB
    sb = get_supabase_admin_client()
    current = sb.table("invitations")\
        .select("id")\
        .eq("email", email)\
        .eq("is_used", False)\
        .execute()

    if not current.data:
        report("Expired invite replaced", False, "No active invite found to expire")
        return None

    expire_invitation(current.data[0]["id"])

    # Now create a new invite — should succeed
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(jwt_token), json={"email": email})
    data = resp.json()

    ok = resp.status_code == 200 and "code" in data and "expires_at" in data
    report(
        "New invite succeeds after expiring the old one",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )

    # Verify the old invite is now marked as used
    old_row = sb.table("invitations")\
        .select("is_used")\
        .eq("id", current.data[0]["id"])\
        .single()\
        .execute()
    old_marked = old_row.data and old_row.data.get("is_used") is True
    report(
        "Expired invite was marked as used by the endpoint",
        old_marked,
        f"old invite is_used={old_row.data.get('is_used') if old_row.data else 'N/A'}",
    )

    return data if ok else None


def test_status_expired(jwt_token: str, email: str):
    """Test 6: Expire the current invite via DB, then check status returns 'expired'."""
    print("\n--- Test 6: Invitation status — expired ---")

    sb = get_supabase_admin_client()
    current = sb.table("invitations")\
        .select("id")\
        .eq("email", email)\
        .eq("is_used", False)\
        .execute()

    if not current.data:
        report("Status expired", False, "No active invite found to expire")
        return

    expire_invitation(current.data[0]["id"])

    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(jwt_token))
    data = resp.json()

    ok = resp.status_code == 200 and data.get("status") == "expired"
    report(
        "Status endpoint returns 'expired' for expired invite",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )


def test_status_used(jwt_token: str, email: str):
    """Test 7: Mark invite as used, then check status returns 'used'."""
    print("\n--- Test 7: Invitation status — used ---")

    # Create a fresh invite first (the current one is expired from test 6)
    url = f"{API_BASE_URL}/api/admin/invite"
    resp = requests.post(url, headers=api_headers(jwt_token), json={"email": email})
    if resp.status_code != 200:
        report("Status used (setup)", False, f"Could not create invite: {resp.status_code} {resp.text}")
        return

    sb = get_supabase_admin_client()
    current = sb.table("invitations")\
        .select("id")\
        .eq("email", email)\
        .eq("is_used", False)\
        .execute()

    if not current.data:
        report("Status used", False, "No active invite found to mark as used")
        return

    mark_invitation_used(current.data[0]["id"])

    url = f"{API_BASE_URL}/api/admin/invitations/status/{email}"
    resp = requests.get(url, headers=api_headers(jwt_token))
    data = resp.json()

    ok = resp.status_code == 200 and data.get("status") == "used"
    report(
        "Status endpoint returns 'used' for used invite",
        ok,
        f"status={resp.status_code} body={json.dumps(data)}",
    )


def test_unauthenticated_access():
    """Test 8: Requests without a valid token are rejected."""
    print("\n--- Test 8: Unauthenticated access rejected ---")

    # POST /invite without token
    resp1 = requests.post(
        f"{API_BASE_URL}/api/admin/invite",
        headers={"Content-Type": "application/json"},
        json={"email": "anyone@example.com"},
    )
    ok1 = resp1.status_code in (401, 403)
    report(
        "POST /invite without token returns 401/403",
        ok1,
        f"status={resp1.status_code}",
    )

    # GET /invitations/status without token
    resp2 = requests.get(
        f"{API_BASE_URL}/api/admin/invitations/status/anyone@example.com",
        headers={"Content-Type": "application/json"},
    )
    ok2 = resp2.status_code in (401, 403)
    report(
        "GET /invitations/status without token returns 401/403",
        ok2,
        f"status={resp2.status_code}",
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
    test_create_invite_success(jwt_token, TARGET_EMAIL)       # 1
    test_duplicate_invite_rejected(jwt_token, TARGET_EMAIL)    # 2
    test_status_active(jwt_token, TARGET_EMAIL)                # 3
    test_status_none(jwt_token)                                # 4
    test_expired_invite_replaced(jwt_token, TARGET_EMAIL)      # 5
    test_status_expired(jwt_token, TARGET_EMAIL)               # 6
    test_status_used(jwt_token, TARGET_EMAIL)                  # 7
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

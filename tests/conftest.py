import os
from pathlib import Path
from typing import Optional

import pytest
import requests
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent.parent
load_dotenv(BASE_DIR / ".env")

SUPABASE_URL = os.getenv("SUPABASE_URL")
SUPABASE_ANON_KEY = os.getenv("SUPABASE_ANON_KEY")
SUPABASE_SERVICE_ROLE_KEY = os.getenv("SUPABASE_SERVICE_ROLE_KEY")

API_BASE_URL = "http://localhost:8000"
_THROWAWAY_EMAIL = "test-throwaway-ci@example.com"
_THROWAWAY_PASSWORD = "Throwaway!123"


def _supabase_admin_headers() -> dict:
    return {
        "apikey": SUPABASE_SERVICE_ROLE_KEY,
        "Authorization": f"Bearer {SUPABASE_SERVICE_ROLE_KEY}",
        "Content-Type": "application/json",
    }


def _force_delete_throwaway() -> None:
    """Best-effort cleanup: remove any leftover throwaway user and invitation.

    Uses the profiles table with PostgREST eq-filtering to safely locate ONLY
    the throwaway user — never touches any other account.
    """
    resp = requests.get(
        f"{SUPABASE_URL}/rest/v1/profiles?email=eq.{_THROWAWAY_EMAIL}&select=id",
        headers=_supabase_admin_headers(),
    )
    if resp.status_code == 200:
        for profile in resp.json():
            requests.delete(
                f"{SUPABASE_URL}/auth/v1/admin/users/{profile['id']}",
                headers=_supabase_admin_headers(),
            )
    requests.delete(
        f"{SUPABASE_URL}/rest/v1/invitations?email=eq.{_THROWAWAY_EMAIL}",
        headers=_supabase_admin_headers(),
    )


# ---------------------------------------------------------------------------
# Unit test fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def jwt_token():
    """Lightweight dummy JWT token for unit tests that only need a token string."""
    return "test-jwt-token"


# ---------------------------------------------------------------------------
# Integration test fixtures
# (require a live server + valid credentials in .env)
# ---------------------------------------------------------------------------

@pytest.fixture(scope="session")
def real_jwt_token() -> str:
    """Authenticate against Supabase and return a real JWT token."""
    email = os.getenv("TEST_ADMIN_EMAIL")
    password = os.getenv("TEST_ADMIN_PASSWORD")

    if not email or not password:
        pytest.skip("Integration test requires TEST_ADMIN_EMAIL and TEST_ADMIN_PASSWORD in .env")

    url = f"{SUPABASE_URL}/auth/v1/token?grant_type=password"
    headers = {"apikey": SUPABASE_ANON_KEY, "Content-Type": "application/json"}
    payload = {"email": email, "password": password}

    try:
        resp = requests.post(url, headers=headers, json=payload, timeout=10)
        resp.raise_for_status()
        token = resp.json().get("access_token")
    except Exception as e:
        pytest.skip(f"Could not authenticate with Supabase: {e}")

    if not token:
        pytest.skip("Supabase returned no access_token — check TEST_ADMIN_EMAIL / TEST_ADMIN_PASSWORD")

    return token


@pytest.fixture(scope="session")
def user_id() -> str:
    value = os.getenv("TEST_TARGET_USER_ID")
    if not value:
        pytest.skip("Integration test requires TEST_TARGET_USER_ID in .env")
    return value


@pytest.fixture(scope="session")
def target_email() -> str:
    value = os.getenv("TEST_TARGET_EMAIL")
    if not value:
        pytest.skip("Integration test requires TEST_TARGET_EMAIL in .env")
    return value


@pytest.fixture(scope="session")
def email() -> str:
    """Invite-flow test email (safe to create/delete invitations for)."""
    return os.getenv("TEST_INVITE_TARGET_EMAIL", "test-invite-flow@example.com")


@pytest.fixture(scope="session")
def full_name() -> str:
    return os.getenv("TEST_FULL_NAME", "Integration Test User")


@pytest.fixture(scope="session")
def reason() -> str:
    return os.getenv("TEST_DEACTIVATE_REASON", "Automated integration test")


# ---------------------------------------------------------------------------
# Throwaway user fixture (for admin user management tests)
# ---------------------------------------------------------------------------

@pytest.fixture()
def throwaway_user_id() -> str:
    """
    Create a disposable Supabase user for admin endpoint tests.

    Uses the service role key directly — no JWT or running app required.

    Setup:
      1. Inserts the throwaway email into the invitations whitelist via the
         Supabase REST API (satisfies the DB trigger on user creation).
      2. Creates the auth user via the Supabase admin API.
    Teardown:
      Deletes the auth user (cascades to profiles) and the invitation record.
      Safe even if the test itself already deleted the user.
    """
    if not SUPABASE_SERVICE_ROLE_KEY:
        pytest.skip("Integration test requires SUPABASE_SERVICE_ROLE_KEY in .env")

    # Clean any leftovers from a previously aborted run
    _force_delete_throwaway()

    # Step 1: insert directly into invitations table via service role REST API
    from datetime import datetime, timezone, timedelta
    expires_at = (datetime.now(timezone.utc) + timedelta(days=7)).isoformat()
    invite_resp = requests.post(
        f"{SUPABASE_URL}/rest/v1/invitations",
        headers={**_supabase_admin_headers(), "Prefer": "return=representation"},
        json={"email": _THROWAWAY_EMAIL, "is_registered": False, "expires_at": expires_at},
    )
    assert invite_resp.status_code in (200, 201), f"Failed to create invitation: {invite_resp.text}"

    # Step 2: create the auth user (DB trigger validates against the invitations whitelist)
    user_resp = requests.post(
        f"{SUPABASE_URL}/auth/v1/admin/users",
        headers=_supabase_admin_headers(),
        json={"email": _THROWAWAY_EMAIL, "password": _THROWAWAY_PASSWORD, "email_confirm": True},
    )
    assert user_resp.status_code == 200, f"Failed to create throwaway user: {user_resp.text}"
    uid = user_resp.json()["id"]

    yield uid

    # Teardown — safe no-op if test already deleted the user
    requests.delete(
        f"{SUPABASE_URL}/auth/v1/admin/users/{uid}",
        headers=_supabase_admin_headers(),
    )
    requests.delete(
        f"{SUPABASE_URL}/rest/v1/invitations?email=eq.{_THROWAWAY_EMAIL}",
        headers=_supabase_admin_headers(),
    )


# ---------------------------------------------------------------------------
# Test ordering
# ---------------------------------------------------------------------------

def pytest_collection_modifyitems(items):
    """Ensure delete_user tests always run last."""
    delete_tests = [item for item in items if "delete_user" in item.nodeid]
    other_tests = [item for item in items if "delete_user" not in item.nodeid]
    items[:] = other_tests + delete_tests

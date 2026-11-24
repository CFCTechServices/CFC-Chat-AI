import sys
import types
from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from app.api.endpoints import auth
from app.config import settings

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(auth.router, prefix="/api/auth")
    client = TestClient(app)
    return client

# Define variables for mock settings, do not depends on actual environment variables.
SUPABASE_URL = "https://test.supabase.co"
SUPABASE_SERVICE_ROLE_KEY = "service-role-key"
FRONTEND_BASE_URL = "http://localhost:8000"
SUPABASE_ANON_KEY = "anon-test"

@pytest.fixture()
def mock_settings(monkeypatch):
    monkeypatch.setattr(settings, "SUPABASE_URL", SUPABASE_URL, raising=False)
    monkeypatch.setattr(settings, "SUPABASE_SERVICE_ROLE_KEY", SUPABASE_SERVICE_ROLE_KEY, raising=False)
    monkeypatch.setattr(settings, "FRONTEND_BASE_URL", FRONTEND_BASE_URL, raising=False)
    monkeypatch.setattr(settings, "SUPABASE_ANON_KEY", SUPABASE_ANON_KEY, raising=False)

class HttpResponse:
    def __init__(self, status_code: int, text: str = ""):
        self.text = text
        self.status_code = status_code


class SupabaseQueryResponse:
    def __init__(self, data):
        self.data = data


class FakeInvitationQuery:
    """A fake Supabase client and query classes for testing"""
    def __init__(self, data=None, error: Exception | None = None):
        self._data = data
        self._error = error
        self.update_payload = None
        self.filters = []

    def select(self, *args, **kwargs):
        return self

    def update(self, payload):
        self.update_payload = payload
        return self

    def eq(self, column, value):
        self.filters.append((column, value))
        return self

    def order(self, *args, **kwargs):
        return self

    def limit(self, *args, **kwargs):
        return self

    def single(self):
        return self

    def execute(self):
        if self._error:
            raise self._error
        return SupabaseQueryResponse(self._data)


class FakeSupabase:
    """A fake Supabase client for testing."""
    def __init__(self, data=None, error: Exception | None = None):
        self.query = FakeInvitationQuery(data=data, error=error)
        self.table_name = None

    def table(self, table_name: str):
        self.table_name = table_name
        return self.query


def patch_supabase_module(monkeypatch, fake_supabase):
    """Patch lazy import target used inside endpoints: app.core.supabase_service."""
    fake_module = types.SimpleNamespace(supabase=fake_supabase)
    monkeypatch.setitem(sys.modules, "app.core.supabase_service", fake_module)


def test_get_auth_config_returns_public_supabase_settings(client, monkeypatch, mock_settings):
    """Test that the auth config endpoint returns the expected public Supabase settings."""
    response = client.get("/api/auth/config")
    
    assert response.status_code == 200
    assert response.json() == {
        "supabaseUrl": SUPABASE_URL,
        "supabaseKey": SUPABASE_ANON_KEY,
    }


def test_forgot_password_returns_success_on_supabase_200(client, monkeypatch, mock_settings):
    """Test that the forgot password endpoint returns success when Supabase responds with 200."""
    captured = {}

    def post(url, json, headers, params):
        captured["url"] = url
        captured["json"] = json
        captured["headers"] = headers
        captured["params"] = params
        return HttpResponse(200, "ok")

    monkeypatch.setattr(auth.http_requests, "post", post)

    response = client.post("/api/auth/forgot-password", json={"email": "user@cfctech.com"})

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "If an account exists with this email, a password reset link has been sent.",
    }
    assert captured["url"] == f"{SUPABASE_URL}/auth/v1/recover"
    assert captured["json"] == {"email": "user@cfctech.com"}
    assert captured["headers"] == {
        "apikey": SUPABASE_SERVICE_ROLE_KEY,
        "Authorization": f"Bearer {SUPABASE_SERVICE_ROLE_KEY}",
        "Content-Type": "application/json",
    }
    assert captured["params"] == {"redirect_to": FRONTEND_BASE_URL}


@pytest.mark.parametrize("status_code, status_message, expected_success, expected_message", 
                         [(429,"Too many requests", False, "Too many reset attempts. Please wait a few minutes before trying again."),
                          (500, "internal error", True, "If an account exists with this email, a password reset link has been sent.")
                          ])
def test_forgot_password_status_code(client, monkeypatch, mock_settings, status_code, status_message, expected_success, expected_message):
    """Test that the forgot password endpoint handles different Supabase response codes correctly."""
    monkeypatch.setattr(
        auth.http_requests,
        "post",
        lambda *args, **kwargs: HttpResponse(status_code, status_message),
    )

    response = client.post("/api/auth/forgot-password", json={"email": "user@cfctech.com"})

    assert response.status_code == 200
    assert response.json() == {
        "success": expected_success,
        "message": expected_message,
    }

def test_forgot_password_returns_generic_success_on_exception(client, monkeypatch, mock_settings):
    """Test that the forgot password endpoint returns generic success message on exception."""
    def raise_error(*args, **kwargs):
        raise RuntimeError("network down")

    monkeypatch.setattr(auth.http_requests, "post", raise_error)

    response = client.post("/api/auth/forgot-password", json={"email": "user@cfctech.com"})

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "If an account exists with this email, a password reset link has been sent.",
    }


def test_forgot_password_missing_email_returns_422(client):
    """Test that the forgot password endpoint returns 422 when email is missing."""
    response = client.post("/api/auth/forgot-password", json={})

    assert response.status_code == 422

from datetime import datetime, timezone, timedelta

def _future_expires_at() -> str:
    return (datetime.now(timezone.utc) + timedelta(days=7)).isoformat()

def _past_expires_at() -> str:
    return "2020-01-01T00:00:00+00:00"


def test_check_email_eligible_when_invited_and_active(client, monkeypatch):
    """Invited email with a non-expired invite returns eligible=True."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(data=[{"expires_at": _future_expires_at(), "is_registered": False}]),
    )

    response = client.post("/api/auth/check-email", json={"email": "invitee@cfctech.com"})

    assert response.status_code == 200
    assert response.json() == {
        "eligible": True,
        "message": "Email is eligible for registration.",
    }


def test_check_email_ineligible_when_not_invited(client, monkeypatch):
    """Email not in invitations table returns eligible=False."""
    patch_supabase_module(monkeypatch, FakeSupabase(data=[]))

    response = client.post("/api/auth/check-email", json={"email": "unknown@cfctech.com"})

    assert response.status_code == 200
    assert response.json()["eligible"] is False
    assert "not been invited" in response.json()["message"].lower()


def test_check_email_ineligible_when_invite_expired(client, monkeypatch):
    """Invited email with an expired invite returns eligible=False."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(data=[{"expires_at": _past_expires_at(), "is_registered": False}]),
    )

    response = client.post("/api/auth/check-email", json={"email": "expired@cfctech.com"})

    assert response.status_code == 200
    assert response.json()["eligible"] is False
    assert "expired" in response.json()["message"].lower()


def test_check_email_ineligible_on_exception(client, monkeypatch):
    """Returns eligible=False gracefully when Supabase throws."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(error=RuntimeError("supabase is down")),
    )

    response = client.post("/api/auth/check-email", json={"email": "error@cfctech.com"})

    assert response.status_code == 200
    assert response.json()["eligible"] is False


def test_check_email_missing_email_returns_422(client):
    """Missing email body field returns 422."""
    response = client.post("/api/auth/check-email", json={})

    assert response.status_code == 422
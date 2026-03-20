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

@pytest.mark.parametrize("supabase_data, invite_code, valid, message, email",
                        [({"email": "invitee@cfctech.com", "is_used": False}, "INVITE-123", True, "Invite code is valid.", "invitee@cfctech.com"),
                         ({"email": "invitee@cfctech.com", "is_used": True}, "INVITE-USED", False, "Invite code has already been used.", None),
                         ])
def test_validate_invite_returns_valid_with_email(client, monkeypatch, mock_settings, supabase_data, invite_code, valid, message, email):
    """Test that the validate invite endpoint returns correct validity and email based on Supabase data."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(data=supabase_data),
    )

    response = client.post("/api/auth/validate-invite", json={"invite_code": invite_code})

    assert response.status_code == 200
    assert response.json() == {
        "valid": valid,
        "message": message,
        "email": email,
    }


def test_validate_invite_returns_invalid_on_exception(client, monkeypatch):
    """Test that the validate invite endpoint returns invalid when an exception occurs."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(error=RuntimeError("supabase is down")),
    )

    response = client.post("/api/auth/validate-invite", json={"invite_code": "INVITE-ERROR"})

    assert response.status_code == 200
    assert response.json() == {
        "valid": False,
        "message": "Invalid invite code.",
        "email": None,
    }


def test_validate_invite_missing_code_returns_422(client):
    """Test that the validate invite endpoint returns 422 when invite code is missing."""
    response = client.post("/api/auth/validate-invite", json={})

    assert response.status_code == 422


def test_mark_invite_used_returns_success_when_updated(client, monkeypatch):
    """Test that the mark invite used endpoint returns success when the invite is marked as used."""
    fake_supabase = FakeSupabase(data=[{"code": "INVITE-123", "is_used": True}])
    patch_supabase_module(monkeypatch, fake_supabase)

    response = client.post("/api/auth/mark-invite-used", json={"invite_code": "INVITE-123"})

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "Invite marked as used.",
    }
    assert fake_supabase.table_name == "invitations"
    assert fake_supabase.query.update_payload == {"is_used": True}
    assert ("code", "INVITE-123") in fake_supabase.query.filters
    assert ("is_used", False) in fake_supabase.query.filters


def test_mark_invite_used_returns_not_found(client, monkeypatch, mock_settings):
    """Test that the mark invite used endpoint returns not found when no invite code matches."""
    patch_supabase_module(monkeypatch, FakeSupabase(data=[]))

    response = client.post("/api/auth/mark-invite-used", json={"invite_code": "INVITE-404"})

    assert response.status_code == 200
    assert response.json() == {
        "success": False,
        "message": "Invite code not found or already used.",
    }


def test_mark_invite_used_returns_500_on_exception(client, monkeypatch):
    """Test that the mark invite used endpoint returns 500 when an exception occurs."""
    patch_supabase_module(
        monkeypatch,
        FakeSupabase(error=RuntimeError("database unavailable")),
    )

    response = client.post("/api/auth/mark-invite-used", json={"invite_code": "INVITE-ERROR"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to mark invite as used."}


def test_mark_invite_used_missing_code_returns_422(client):
    """Test that the mark invite used endpoint returns 422 when invite code is missing."""
    response = client.post("/api/auth/mark-invite-used", json={})

    assert response.status_code == 422
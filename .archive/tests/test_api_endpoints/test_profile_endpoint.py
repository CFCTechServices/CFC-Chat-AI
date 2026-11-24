import types
from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from app.api.endpoints import profile


@pytest.fixture()
def dependency_state():
    """Fixture to hold dependency override state."""
    return {
        "current_user": types.SimpleNamespace(id="user-123"),
        "user_client": None,
    }

@pytest.fixture()
def client(dependency_state):
    app = FastAPI()
    app.include_router(profile.router, prefix="/api/profile")
    """Use to hold and inject dependency override state."""
    app.dependency_overrides[profile.get_current_user] = lambda: dependency_state["current_user"]
    app.dependency_overrides[profile.get_user_client] = lambda: dependency_state["user_client"]
    client = TestClient(app)
    return client


class SupabaseQueryResponse:
    def __init__(self, data):
        self.data = data


class FakeProfileQuery:
    """A fake Supabase client and query classes for profiles."""
    def __init__(self, data=None, error: Exception | None = None):
        self._data = data
        self._error = error
        self.selected_columns = None
        self.update_payload = None
        self.filters = []
        self.single_called = False

    def select(self, *columns):
        self.selected_columns = columns
        return self

    def update(self, payload):
        self.update_payload = payload
        return self

    def eq(self, column, value):
        self.filters.append((column, value))
        return self

    def single(self):
        self.single_called = True
        return self

    def execute(self):
        if self._error:
            raise self._error
        return SupabaseQueryResponse(self._data)


class FakeUserClient:
    """"A fake Supabase client for testing."""
    def __init__(self, table_queries):
        self.table_queries = {name: list(queries) for name, queries in table_queries.items()}
        self.table_calls = []

    def table(self, table_name: str):
        self.table_calls.append(table_name)
        queue = self.table_queries.get(table_name, [])
        if not queue:
            raise AssertionError(f"Unexpected table call: {table_name}")
        query = queue.pop(0)
        self.table_queries[table_name] = queue
        return query


def make_profile(**kwargs):
    """Use to set up a profile structure."""
    base = {
        "id": "user-123",
        "email": "user@cfctech.com",
        "full_name": "Chira Foster",
        "avatar_url": "https://cdn.example.com/avatar.jpg",
        "role": "user",
        "status": "active",
        "created_at": "2026-03-17T09:00:00Z",
    }
    base.update(kwargs)
    return base


def test_get_profile_returns_current_profile(client, dependency_state):
    """Test that getting the current profile returns the correct profile data."""
    profile_query = FakeProfileQuery(data=make_profile())
    dependency_state["user_client"] = FakeUserClient({"profiles": [profile_query]})

    response = client.get("/api/profile/me")

    assert response.status_code == 200
    assert response.json() == make_profile()
    assert profile_query.selected_columns == ("*",)
    assert profile_query.filters == [("id", "user-123")]
    assert profile_query.single_called is True


def test_get_profile_returns_404_when_profile_not_found(client, dependency_state):
    """Test that getting the current profile returns 404 when the profile is not found."""
    dependency_state["user_client"] = FakeUserClient({"profiles": [FakeProfileQuery(data=None)]})

    response = client.get("/api/profile/me")

    assert response.status_code == 404
    assert response.json() == {"detail": "Profile not found"}


def test_get_profile_returns_500_on_exception(client, dependency_state):
    """Test that getting the current profile returns 500 on unexpected database exception."""
    dependency_state["user_client"] = FakeUserClient(
        {"profiles": [FakeProfileQuery(error=RuntimeError("database unavailable"))]}
    )

    response = client.get("/api/profile/me")

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to get profile: database unavailable"}


@pytest.mark.parametrize(
    ("payload", "updated_profile", "expected_update_payload"),
    [
        (
            {"full_name": "Alice Bobb"},
            make_profile(full_name="Alice Bobb"),
            {"full_name": "Alice Bobb"},
        ),
        (
            {"avatar_url": "https://cdn.example.com/new-avatar.png"},
            make_profile(avatar_url="https://cdn.example.com/new-avatar.png"),
            {"avatar_url": "https://cdn.example.com/new-avatar.png"},
        ),
        (
            {
                "full_name": "Alice Bobb",
                "avatar_url": "https://cdn.example.com/new-avatar.png",
            },
            make_profile(
                full_name="Alice Bobb",
                avatar_url="https://cdn.example.com/new-avatar.png",
            ),
            {
                "full_name": "Alice Bobb",
                "avatar_url": "https://cdn.example.com/new-avatar.png",
            },
        ),
    ],
)
def test_update_profile_returns_updated_profile(
    client,
    dependency_state,
    payload,
    updated_profile,
    expected_update_payload,
):
    """Use to test that updating the profile in the database returns the updated profile."""
    update_query = FakeProfileQuery(data=[updated_profile])
    dependency_state["user_client"] = FakeUserClient({"profiles": [update_query]})

    response = client.patch("/api/profile/me", json=payload)

    assert response.status_code == 200
    assert response.json() == updated_profile
    assert update_query.update_payload == expected_update_payload
    assert update_query.filters == [("id", "user-123")]


def test_update_profile_returns_400_when_no_fields_provided(client, dependency_state):
    """Test that updating the profile returns 400 when no fields are provided."""
    dependency_state["user_client"] = FakeUserClient({"profiles": []})

    response = client.patch("/api/profile/me", json={})

    assert response.status_code == 400
    assert response.json() == {
        "detail": "No fields to update. Provide full_name or avatar_url."
    }


def test_update_profile_returns_500_when_update_returns_no_data(client, dependency_state):
    """Test that updating the profile returns 500 when the update returns no data (no existence profile)."""
    dependency_state["user_client"] = FakeUserClient({"profiles": [FakeProfileQuery(data=[])]})

    response = client.patch("/api/profile/me", json={"full_name": "Alice Bobb"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to update profile"}


def test_update_profile_returns_500_on_exception(client, dependency_state):
    """Test that updating the profile returns 500 on unexpected database exception."""
    dependency_state["user_client"] = FakeUserClient(
        {"profiles": [FakeProfileQuery(error=RuntimeError("update failed"))]}
    )

    response = client.patch("/api/profile/me", json={"full_name": "Alice Bobb"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to update profile: update failed"}


def test_complete_profile_returns_updated_profile(client, dependency_state):
    """Test that calling complete profile returns the updated profile."""
    updated_profile = make_profile(full_name="Alice Bobb")
    update_query = FakeProfileQuery(data=[updated_profile])
    dependency_state["user_client"] = FakeUserClient({"profiles": [update_query]})

    response = client.post("/api/profile/complete", json={"full_name": "Alice Bobb"})

    assert response.status_code == 200
    assert response.json() == updated_profile
    assert update_query.update_payload == {"full_name": "Alice Bobb"}
    assert update_query.filters == [("id", "user-123")]


def test_complete_profile_returns_400_when_no_fields_provided(client, dependency_state):
    """Test that calling complete profile returns 400 when no fields are provided."""
    dependency_state["user_client"] = FakeUserClient({"profiles": []})

    response = client.post("/api/profile/complete", json={})

    assert response.status_code == 400
    assert response.json() == {
        "detail": "No fields to update. Provide full_name or avatar_url."
    }


def test_complete_profile_returns_500_on_exception(client, dependency_state):
    """Test that calling complete profile returns 500 on unexpected database exception."""
    dependency_state["user_client"] = FakeUserClient(
        {"profiles": [FakeProfileQuery(error=RuntimeError("update failed"))]}
    )

    response = client.post("/api/profile/complete", json={"full_name": "Alice Bobb"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to update profile: update failed"}
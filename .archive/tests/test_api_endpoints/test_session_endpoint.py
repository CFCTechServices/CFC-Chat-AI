import types

from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.api.endpoints import sessions
import pytest

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
    app.include_router(sessions.router, prefix="/api")
    """Use to hold and inject dependency override state."""
    app.dependency_overrides[sessions.get_current_user] = lambda: dependency_state["current_user"]
    app.dependency_overrides[sessions.get_user_client] = lambda: dependency_state["user_client"]
    client = TestClient(app)
    return client


class SupabaseQueryResponse:
    def __init__(self, data):
        self.data = data


class FakeSessionQuery:
    """A fake Supabase client and query classes for chat sessions and messages"""
    def __init__(self, data=None, error: Exception | None = None):
        self._data = data if data is not None else []
        self._error = error
        self.selected_columns = None
        self.inserted_payload = None
        self.update_payload = None
        self.filters = []
        self.order_by = None
        self.limit_value = None

    def select(self, *columns):
        self.selected_columns = columns
        return self

    def insert(self, payload):
        self.inserted_payload = payload
        return self

    def update(self, payload):
        self.update_payload = payload
        return self

    def delete(self):
        return self

    def eq(self, column, value):
        self.filters.append((column, value))
        return self

    def order(self, column, desc=False):
        self.order_by = (column, desc)
        return self

    def limit(self, value):
        self.limit_value = value
        return self

    def execute(self):
        if self._error:
            raise self._error
        return SupabaseQueryResponse(self._data)


class FakeUserClient:
    "A fake Supabase client for testing"
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


def make_session(**kwargs):
    "Use to set up a session structure"
    base = {
        "id": "session-123",
        "user_id": "user-123",
        "title": "Test Session",
        "created_at": "2026-03-17T09:00:00Z",
    }
    base.update(kwargs)
    return base


def make_message(**kwargs):
    "Use to set up a message structure from user"
    base = {
        "id": "msg-1",
        "session_id": "session-123",
        "role": "user",
        "content": "What is beef nutrition?",
        "created_at": "2026-03-17T09:00:00Z",
        "metadata": None,
    }
    base.update(kwargs)
    return base


def test_get_sessions_returns_sessions(client, dependency_state):
    """Test that getting all sessions in an account returns the expected sessions."""
    sessions_data = [make_session(), make_session(id="session-2", title="Second Session")]
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=sessions_data)]
    })

    response = client.get("/api/sessions")

    assert response.status_code == 200
    assert response.json() == sessions_data


def test_get_sessions_returns_empty_list(client, dependency_state):
    """Test that getting all sessions in an account returns an empty list when there are no sessions."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[])]
    })

    response = client.get("/api/sessions")

    assert response.status_code == 200
    assert response.json() == []


def test_get_sessions_returns_500_on_exception(client, dependency_state):
    """Test that getting all sessions in an account returns a 500 error when there is a database error."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(error=RuntimeError("DB error"))]
    })

    response = client.get("/api/sessions")

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}



def test_get_sessions_detail_returns_sessions_with_message_count(client, dependency_state):
    """Test that getting all sessions with detail=true returns sessions with message count and last message."""
    sessions_data = [
        {
            **make_session(),
            "chat_messages": [{"count": 10000}],
        }
    ]
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=sessions_data)],
        "chat_messages": [FakeSessionQuery(data=[{"content": "What is beef nutrition?"}])],
    })

    response = client.get("/api/sessions?detail=true")

    assert response.status_code == 200
    body = response.json()
    assert body[0]["message_count"] == 10000
    assert body[0]["last_message"] == "What is beef nutrition?"
    assert "chat_messages" not in body[0]


def test_get_sessions_detail_returns_sessions_with_no_messages(client, dependency_state):
    """Test that getting all sessions with detail=true returns sessions with no messages."""
    sessions_data = [
        {
            **make_session(),
            "chat_messages": [{"count": 0}],
        }
    ]
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=sessions_data)],
    })

    response = client.get("/api/sessions?detail=true")

    assert response.status_code == 200
    body = response.json()
    assert body[0]["message_count"] == 0
    assert body[0]["last_message"] is None


def test_get_sessions_detail_returns_500_on_exception(client, dependency_state):
    """Test that getting all sessions with detail=true returns a 500 error when there is a database error."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(error=RuntimeError("DB error"))]
    })

    response = client.get("/api/sessions?detail=true")

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}


def test_create_session_returns_new_session(client, dependency_state):
    """Test that creating a new session returns the created session recently."""
    new_session = make_session()
    insert_query = FakeSessionQuery(data=[new_session])
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [insert_query]
    })

    response = client.post("/api/sessions", json={"title": "New Session"})

    assert response.status_code == 200
    assert response.json() == new_session
    assert insert_query.inserted_payload == {"user_id": "user-123", "title": "New Session"}


def test_create_session_with_default_title(client, dependency_state):
    """Test that creating a new session with no title uses the default title."""
    new_session = make_session(title="New Chat")
    insert_query = FakeSessionQuery(data=[new_session])
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [insert_query]
    })

    response = client.post("/api/sessions", json={})

    assert response.status_code == 200
    assert insert_query.inserted_payload == {"user_id": "user-123", "title": "New Chat"}


def test_create_session_returns_500_when_insert_returns_no_data(client, dependency_state):
    """Test that creating a new session returns a 500 error when the insert returns no data (no user_id)."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[])]
    })

    response = client.post("/api/sessions", json={"title": "New Chat"})

    assert response.status_code == 500
    assert "Failed to create session" in response.json()["detail"]


def test_create_session_returns_500_on_exception(client, dependency_state):
    """Test that creating a new session returns a 500 error when there is a database error."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(error=RuntimeError("DB error"))]
    })

    response = client.post("/api/sessions", json={"title": "New Chat"})

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}


def test_get_session_history_returns_messages(client, dependency_state):
    """Test that getting the message history for a session returns all the messages belongs to the session."""
    messages = [make_message(), make_message(id="msg-2", content="More details please")]
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[{"id": "session-123"}])],
        "chat_messages": [FakeSessionQuery(data=messages)],
    })

    response = client.get("/api/sessions/session-123")

    assert response.status_code == 200
    assert response.json() == messages


def test_get_session_history_returns_404_when_session_not_found(client, dependency_state):
    """Test that getting the message history for a session returns a 404 error when the session is not found."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[])]
    })

    response = client.get("/api/sessions/nonexistent")

    assert response.status_code == 404
    assert response.json() == {"detail": "Session not found"}


def test_get_session_history_returns_500_on_exception(client, dependency_state):
    """Test that getting the message history for a session returns a 500 error when there is a database error."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[{"id": "session-123"}])],
        "chat_messages": [FakeSessionQuery(error=RuntimeError("DB error"))],
    })

    response = client.get("/api/sessions/session-123")

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}


def test_delete_session_returns_success(client, dependency_state):
    """Test that deleting a session returns success."""
    verify_query = FakeSessionQuery(data=[{"id": "session-123"}])
    msgs_query = FakeSessionQuery(data=[])
    session_query = FakeSessionQuery(data=[])
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [verify_query, session_query],
        "chat_messages": [msgs_query],
    })

    response = client.delete("/api/sessions/session-123")

    assert response.status_code == 200
    assert response.json() == {"success": True}


def test_delete_session_returns_404_when_not_found(client, dependency_state):
    """Test that deleting a session returns a 404 error when the session is not found."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[])]
    })

    response = client.delete("/api/sessions/nonexistent")

    assert response.status_code == 404
    assert response.json() == {"detail": "Session not found"}


def test_delete_session_returns_500_on_exception(client, dependency_state):
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(error=RuntimeError("DB error"))]
    })

    response = client.delete("/api/sessions/session-123")

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}



def test_update_session_title_returns_updated_session(client, dependency_state):
    """Test that updating a session title returns the updated session."""
    updated_session = make_session(title="Updated Title")
    verify_query = FakeSessionQuery(data=[{"id": "session-123"}])
    update_query = FakeSessionQuery(data=[updated_session])
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [verify_query, update_query],
    })

    response = client.patch("/api/sessions/session-123", json={"title": "Updated Title"})

    assert response.status_code == 200
    assert response.json() == updated_session
    assert update_query.update_payload == {"title": "Updated Title"}
    assert ("id", "session-123") in update_query.filters


def test_update_session_title_returns_404_when_not_found(client, dependency_state):
    """Test that updating a session title returns a 404 error when the session is not found."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(data=[])]
    })

    response = client.patch("/api/sessions/nonexistent", json={"title": "Updated Title"})

    assert response.status_code == 404
    assert response.json() == {"detail": "Session not found or you don't have permission to update it"}


def test_update_session_title_returns_500_when_update_returns_no_data(client, dependency_state):
    """Test that updating a session title returns a 500 error when the update returns no data ( no session id)."""
    verify_query = FakeSessionQuery(data=[{"id": "session-123"}])
    update_query = FakeSessionQuery(data=[])
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [verify_query, update_query],
    })

    response = client.patch("/api/sessions/session-123", json={"title": "Updated Title"})

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to update session title"}


def test_update_session_title_returns_500_on_exception(client, dependency_state):
    """Test that updating a session title returns a 500 error when there is a database error."""
    dependency_state["user_client"] = FakeUserClient({
        "chat_sessions": [FakeSessionQuery(error=RuntimeError("DB error"))]
    })

    response = client.patch("/api/sessions/session-123", json={"title": "Updated Title"})

    assert response.status_code == 500
    assert response.json() == {"detail": "DB error"}


def test_update_session_title_missing_title_returns_422(client, dependency_state):
    """Test that updating a session title without providing a title returns a 422 error."""
    dependency_state["user_client"] = FakeUserClient({})

    response = client.patch("/api/sessions/session-123", json={})

    assert response.status_code == 422
import types
from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from app.api.endpoints import chat
from app.services.content_repository import ContentRepository


@pytest.fixture()
def client():
	app = FastAPI()
	app.include_router(chat.router, prefix="/api/chat")
	"""Use to hold and inject dependency override state."""
	app.dependency_overrides[chat.get_current_user] = lambda: types.SimpleNamespace(id="user-123")
	client = TestClient(app)
	return client


@pytest.fixture()
def local_content_root(monkeypatch, tmp_path):
	"""Fixture to set up a temporary local content root for testing image serving"""
	monkeypatch.setattr(chat.settings, "LOCAL_CONTENT_ROOT", tmp_path, raising=False)
	monkeypatch.setattr(chat, "_content_repository", ContentRepository(root=tmp_path))
	return tmp_path


class SupabaseQueryResponse:
	def __init__(self, data):
		self.data = data


class FakeTableQuery:
	"""A fake Supabase client and query classes for testing"""
	def __init__(self, data=None, error: Exception | None = None):
		self._data = data if data is not None else []
		self._error = error
		self.inserted_payload = None
		self.upsert_payload = None
		self.filters = []
		self.order_by = None
		self.limit_value = None

	def select(self, *args, **kwargs):
		return self

	def insert(self, payload):
		self.inserted_payload = payload
		return self

	def upsert(self, payload):
		self.upsert_payload = payload
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


class FakeSupabase:
	"""A fake Supabase client for testing"""
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


def make_search_result(**kwargs):
	"""Use for setting up a response structure from chatbot"""
	base = {
		"rank": 1,
		"score": 0.92,
		"text": "Feed formula guidance",
		"source": "nutrition-guide.pdf",
		"chunk_id": "chunk-1",
	}
	base.update(kwargs)
	return base


def test_send_message_returns_assistant_message(client, monkeypatch):
	"""test that sending a message returns the expected assistant reply"""
	session_query = FakeTableQuery(data=[{"id": "session-123"}])
	user_insert_query = FakeTableQuery(data=[{"id": "user-msg-123"}])
	history_query = FakeTableQuery(
		data=[
			{"role": "assistant", "content": "newest reply"},
			{"role": "user", "content": "older question"},
		]
	)
	assistant_insert_query = FakeTableQuery(
		data=[{"id": "assistant-msg-1", "created_at": "2026-03-17T09:00:00Z"}]
	)
	fake_supabase = FakeSupabase(
		{
			"chat_sessions": [session_query],
			"chat_messages": [user_insert_query, history_query, assistant_insert_query],
		}
	)
	monkeypatch.setattr(chat, "supabase", fake_supabase)

	captured = {}

	def fake_ask(question, top_k=None, conversation_history=None):
		captured["question"] = question
		captured["top_k"] = top_k
		captured["conversation_history"] = conversation_history
		return {
			"success": True,
			"answer": "Kali and  Natri Sunfat.",
			"context_used": [{"source": "nutrition-guide.pdf"}],
		}

	monkeypatch.setattr(chat.chat_service, "ask_question", fake_ask)

	response = client.post(
		"/api/chat/message",
		json={"session_id": "session-1", "content": "What is beef nutrition?"},
	)

	assert response.status_code == 200
	assert response.json() == {
		"id": "assistant-msg-1",
		"role": "assistant",
		"content": "Kali and  Natri Sunfat.",
		"citations": [{"source": "nutrition-guide.pdf"}],
		"created_at": "2026-03-17T09:00:00Z",
	}
	assert captured["question"] == "What is beef nutrition?"
	assert captured["top_k"] == chat.settings.DEFAULT_TOP_K
	assert captured["conversation_history"] == [
		{"role": "user", "content": "older question"},
		{"role": "assistant", "content": "newest reply"},
	]
	assert user_insert_query.inserted_payload == {
		"session_id": "session-1",
		"role": "user",
		"content": "What is beef nutrition?",
	}
	assert assistant_insert_query.inserted_payload == {
		"session_id": "session-1",
		"role": "assistant",
		"content": "Kali and  Natri Sunfat.",
		"metadata": {"citations": [{"source": "nutrition-guide.pdf"}]},
	}
	assert history_query.order_by == ("created_at", True)
	assert history_query.limit_value == 10


def test_send_message_returns_404_when_session_not_found(client, monkeypatch):
	"""Test that sending a message returns 404 when the session is not found"""
	fake_supabase = FakeSupabase({"chat_sessions": [FakeTableQuery(data=[])]})
	monkeypatch.setattr(chat, "supabase", fake_supabase)

	ask_called = {"value": False}

	def fake_ask(*args, **kwargs):
		"""Use to verify that ask_question is not called when session is missing or not found"""
		ask_called["value"] = True
		return {"success": True, "answer": "not-used", "context_used": []}

	monkeypatch.setattr(chat.chat_service, "ask_question", fake_ask)

	response = client.post(
		"/api/chat/message",
		json={"session_id": "session-unknown", "content": "What is beef nutrition?"},
	)

	assert response.status_code == 404
	assert response.json() == {"detail": "Session not found"}
	assert ask_called["value"] is False


def test_send_message_returns_500_when_rag_fails(client, monkeypatch):
	"""Test that sending a message returns 500 when the RAG service fails"""
	fake_supabase = FakeSupabase(
		{
			"chat_sessions": [FakeTableQuery(data=[{"id": "session-123"}])],
			"chat_messages": [
				FakeTableQuery(data=[{"id": "user-msg-123"}]),
				FakeTableQuery(data=[]),
			],
		}
	)
	monkeypatch.setattr(chat, "supabase", fake_supabase)
	monkeypatch.setattr(
		chat.chat_service,
		"ask_question",
		lambda *args, **kwargs: {"success": False, "error": "rag failed"},
	)

	response = client.post(
		"/api/chat/message",
		json={"session_id": "session-123", "content": "Explain cfctech company"},
	)

	assert response.status_code == 500
	assert response.json() == {"detail": "rag failed"}


def test_send_message_returns_500_on_unexpected_exception(client, monkeypatch):
	"""Test that sending a message returns 500 on unexpected database exception"""
	fake_supabase = FakeSupabase(
		{
			"chat_sessions": [FakeTableQuery(data=[{"id": "session-123"}])],
			"chat_messages": [FakeTableQuery(error=RuntimeError("database unavailable"))],
		}
	)
	monkeypatch.setattr(chat, "supabase", fake_supabase)

	response = client.post(
		"/api/chat/message",
		json={"session_id": "session-123", "content": "Explain cfctech company"},
	)

	assert response.status_code == 500
	assert response.json() == {"detail": "database unavailable"}


def test_send_message_missing_field_returns_422(client):
	"""Test that sending a message with missing fields returns 422"""
	response = client.post("/api/chat/message", json={"session_id": "session-123"})

	assert response.status_code == 422

@pytest.mark.parametrize("rating", [-1, 1])
def test_submit_feedback_returns_success(client, monkeypatch, rating):
	"""Test that submitting positive or negative feedback returns success and stores the feedback"""
	feedback_query = FakeTableQuery(data=[{"message_id": "msg-123"}])
	fake_supabase = FakeSupabase({"feedback": [feedback_query]})
	monkeypatch.setattr(chat, "supabase", fake_supabase)

	response = client.post(
		"/api/chat/feedback",
		json={"message_id": "msg-123", "session_id": "session-123", "rating": rating},
	)

	assert response.status_code == 200
	assert response.json() == {"success": True}
	assert feedback_query.upsert_payload == {
		"message_id": "msg-123",
		"user_id": "user-123",
		"score": rating,
	}

@pytest.mark.parametrize("rating", [-1, 1])
def test_submit_feedback_returns_500_on_exception(client, monkeypatch, rating):
	"""Test that submitting positive or negative feedback returns 500 on unexpected database exception"""
	fake_supabase = FakeSupabase({"feedback": [FakeTableQuery(error=RuntimeError("database connection failure"))]})
	monkeypatch.setattr(chat, "supabase", fake_supabase)

	response = client.post(
		"/api/chat/feedback",
		json={"message_id": "msg-123", "session_id": "session-123", "rating": rating},
	)

	assert response.status_code == 500
	assert response.json() == {"detail": "database connection failure"}

def test_submit_feedback_missing_field_returns_422(client):
    """Test that submitting feedback and call feedback endpoint without rating fields returns 422"""
    response = client.post("/api/chat/feedback", json={"message_id": "msg-123", "session_id": "session-123"})
    assert response.status_code == 422
	
def test_search_documents_returns_expected_payload(client, monkeypatch):
	"""Test that searching documents returns expected payload"""
	monkeypatch.setattr(
		chat.chat_service,
		"search_documents",
		lambda *args, **kwargs: {
			"success": True,
			"results": [make_search_result()],
		},
	)

	response = client.post("/api/chat/search", json={"query": "chicken nutrition", "top_k": 5})

	assert response.status_code == 200
	body = response.json()
	assert body["success"] is True
	assert body["query"] == "chicken nutrition"
	assert body["total_results"] == 1
	assert body["results"][0]["source_type"] == "document"
	assert body["results"][0]["chunk_id"] == "chunk-1"


def test_search_documents_returns_500_when_service_fails(client, monkeypatch):
	"""Test that searching documents returns 500 when the search service fails"""
	monkeypatch.setattr(
		chat.chat_service,
		"search_documents",
		lambda *_args, **_kwargs: {"success": False, "error": "search failed"},
	)

	response = client.post("/api/chat/search", json={"query": "beef or chicken is better?"})

	assert response.status_code == 500
	assert response.json() == {"detail": "search failed"}


def test_search_documents_returns_500_on_exception(client, monkeypatch):
	"""Test that searching documents returns 500 on unexpected exception"""

	def raise_error(*args, **kwargs):
		raise RuntimeError("search crashed")

	monkeypatch.setattr(chat.chat_service, "search_documents", raise_error)

	response = client.post("/api/chat/search", json={"query": "beef or chicken is better?"})

	assert response.status_code == 500
	assert response.json() == {"detail": "search crashed"}


def test_search_documents_missing_query_returns_422(client):
	"""Test that searching documents returns 422 when the query is missing"""
	response = client.post("/api/chat/search", json={})

	assert response.status_code == 422


def test_ask_question_returns_expected_payload(client, monkeypatch):
	"""Test that asking a question returns expected payload"""
	captured = {}

	def fake_ask(question, top_k=None, conversation_history=None):
		captured["question"] = question
		captured["top_k"] = top_k
		captured["conversation_history"] = conversation_history
		return {
			"success": True,
			"answer": "Kali and Natri Sunfat",
			"context_used": [make_search_result(source_type="document")],
			"confidence": 0.81,
			"relevant_images": [
				{
					"path": "docs/doc-1/images/chart.png",
					"position": 12,
					"alt_text": "Lysine chart",
					"relevance_score": 0.88,
					"context_text": "beef nutrition",
				}
			],
			"answer_video_url": "https://cdn.example.com/video.mp4",
			"answer_start_seconds": 9.0,
			"answer_end_seconds": 14.0,
			"answer_timestamp": "00:09",
			"answer_end_timestamp": "00:14",
		}

	monkeypatch.setattr(chat.chat_service, "ask_question", fake_ask)

	response = client.post(
		"/api/chat/ask",
		json={
			"question": "What is beef nutrition?",
			"top_k": 4,
			"conversation_history": [{"role": "user", "content": "Need kali and natri sunfat guidance"}],
		},
	)

	assert response.status_code == 200
	body = response.json()
	assert body["success"] is True
	assert body["answer"] == "Kali and Natri Sunfat"
	assert body["context_used"][0]["source_type"] == "document"
	assert body["relevant_images"][0]["path"] == "docs/doc-1/images/chart.png"
	assert body["answer_video_url"] == "https://cdn.example.com/video.mp4"
	assert captured["question"] == "What is beef nutrition?"
	assert captured["top_k"] == 4
	assert captured["conversation_history"] == [{"role": "user", "content": "Need kali and natri sunfat guidance"}]

def test_ask_question_with_conversation_history_returns_expected_payload(client, monkeypatch):
	"""Test that asking a question with conversation history returns expected payload"""
	captured = {}

	def fake_ask(question, top_k=None, conversation_history=None):
		captured["question"] = question
		captured["top_k"] = top_k
		captured["conversation_history"] = conversation_history
		return {
			"success": True,
			"answer": "Conversation-aware answer.",
			"context_used": [make_search_result(source_type="document")],
			"confidence": 0.73,
		}

	monkeypatch.setattr(chat.chat_service, "ask_question", fake_ask)

	response = client.post(
		"/api/chat/ask",
		json={
			"question": "Explain more about beef nutrition",
			"top_k": 2,
			"conversation_history": [
				{"role": "user", "content": "What is beef nutrition?"},
				{"role": "assistant", "content": "Kali and Natri Sunfat."},
			],
		},
	)

	assert response.status_code == 200
	assert response.json()["answer"] == "Conversation-aware answer."
	assert captured["question"] == "Explain more about beef nutrition"
	assert captured["top_k"] == 2
	assert captured["conversation_history"] == [
		{"role": "user", "content": "What is beef nutrition?"},
		{"role": "assistant", "content": "Kali and Natri Sunfat."},
	]


def test_ask_question_returns_500_when_service_fails(client, monkeypatch):
	"""Test that asking a question returns 500 when the service fails"""
	monkeypatch.setattr(
		chat.chat_service,
		"ask_question",
		lambda *args, **kwargs: {"success": False, "error": "ask failed"},
	)

	response = client.post("/api/chat/ask", json={"question": "What is feed conversion ratio?"})

	assert response.status_code == 500
	assert response.json() == {"detail": "ask failed"}

    
def test_ask_question_returns_500_on_exception(client, monkeypatch):
	"""Test that asking a question returns 500 on unexpected exception"""
	def raise_error(*args, **kwargs):
		raise RuntimeError("ask crashed")

	monkeypatch.setattr(chat.chat_service, "ask_question", raise_error)

	response = client.post("/api/chat/ask", json={"question": "What is feed conversion ratio?"})

	assert response.status_code == 500
	assert response.json() == {"detail": "ask crashed"}


def test_ask_question_missing_question_returns_422(client):
	"""Test that asking a question returns 422 when the question is missing"""
	response = client.post("/api/chat/ask", json={})

	assert response.status_code == 422


def test_ask_video_question_returns_expected_payload(client, monkeypatch):
	"""Test that asking a video question returns expected payload"""
	monkeypatch.setattr(
		chat.chat_service,
		"ask_video_question",
		lambda *args, **kwargs: {
			"success": True,
			"answer": "Review the clip around 00:40.",
			"context_used": [
				make_search_result(
					source_type="video",
					video_url="https://cdn.example.com/video.mp4",
					start_seconds=40.0,
					end_seconds=58.0,
				)
			],
			"confidence": 0.75,
			"video_context": [
				{
					"video_url": "https://cdn.example.com/video.mp4",
					"start_seconds": 40.0,
					"end_seconds": 58.0,
					"timestamp": "00:40",
					"end_timestamp": "00:58",
				}
			],
			"answer_video_url": "https://cdn.example.com/video.mp4",
			"answer_start_seconds": 40.0,
			"answer_end_seconds": 58.0,
			"answer_timestamp": "00:40",
			"answer_end_timestamp": "00:58",
		},
	)

	response = client.post("/api/chat/ask/video", json={"question": "Show broiler mixing video"})

	assert response.status_code == 200
	body = response.json()
	assert body["success"] is True
	assert body["context_used"][0]["source_type"] == "video"
	assert body["video_context"][0]["video_url"] == "https://cdn.example.com/video.mp4"
	assert body["answer_video_url"] == "https://cdn.example.com/video.mp4"


def test_ask_video_question_returns_500_when_service_fails(client, monkeypatch):
	"""Test that asking a video question returns 500 when the service fails"""
	monkeypatch.setattr(
		chat.chat_service,
		"ask_video_question",
		lambda *args, **kwargs: {"success": False},
	)

	response = client.post("/api/chat/ask/video", json={"question": "Show pellet process video"})

	assert response.status_code == 500
	assert response.json() == {"detail": "Unable to answer video question"}


def test_get_recommendations_returns_expected_payload(client, monkeypatch):
	"""Test that getting recommendations returns expected payload"""
	monkeypatch.setattr(
		chat.chat_service,
		"get_recommendations",
		lambda *args, **kwargs: {
			"success": True,
			"recommendations": {
				"documents": [
					{
						"title": "Beef Nutrition Plan",
						"relevance_score": 0.91,
						"preview": "Beef nutrition guideline",
						"source_type": "document",
					}
				],
				"videos": [],
				"related_topics": [],
			},
			"total_items": 1,
		},
	)

	response = client.post(
		"/api/chat/recommendations",
		json={"query": "beef nutrition", "content_type": "documents"},
	)

	assert response.status_code == 200
	body = response.json()
	assert body["success"] is True
	assert body["total_items"] == 1
	assert body["recommendations"]["documents"][0]["title"] == "Beef Nutrition Plan"


def test_get_recommendations_returns_500_when_service_fails(client, monkeypatch):
	"""Test that getting recommendations returns 500 when the service fails"""
	monkeypatch.setattr(
		chat.chat_service,
		"get_recommendations",
		lambda *args, **kwargs: {"success": False, "error": "recommendation failed"},
	)

	response = client.post("/api/chat/recommendations", json={"query": "beef nutrition"})

	assert response.status_code == 500
	assert response.json() == {"detail": "recommendation failed"}


def test_get_recommendations_missing_query_returns_422(client):
	"""Test that getting recommendations returns 422 when the query is missing"""
	response = client.post("/api/chat/recommendations", json={})

	assert response.status_code == 422


def test_serve_image_rejects_invalid_prefix(client):
	"""Test that serving an image rejects invalid path prefix"""
	response = client.get("/api/chat/content/images/invalid/path.png")

	assert response.status_code == 400
	assert response.json() == {"detail": "Invalid image path format"}


def test_serve_image_rejects_invalid_structure(client, local_content_root):
	"""Test that serving an image rejects invalid path structure"""
	response = client.get("/api/chat/content/images/docs/doc123/not-images/path.png")

	assert response.status_code == 400
	assert response.json() == {"detail": "Invalid image path format"}


def test_serve_image_blocks_path_traversal(client, local_content_root):
	"""Test that serving an image blocks path traversal attempts"""
	# Use percent-encoded dots (%2e%2e) so the HTTP client does not normalize them
	# away before they reach the endpoint. FastAPI decodes them in the path parameter,
	# so the security check (resolve().relative_to()) fires and returns 403.
	response = client.get("/api/chat/content/images/docs/doc123/images/%2e%2e/%2e%2e/%2e%2e/hidden.png")

	assert response.status_code == 403
	assert response.json() == {"detail": "Access denied"}


def test_serve_image_returns_404_for_missing_file(client, local_content_root):
	"""Test that serving an image returns 404 for a missing file"""
	response = client.get("/api/chat/content/images/docs/doc123/images/missing.png")

	assert response.status_code == 404
	assert response.json() == {"detail": "Image not found"}


def test_serve_image_returns_existing_file(client, local_content_root):
	"""Test that serving an image returns an existing file"""
	image_path = local_content_root / "doc123" / "images" / "feed.jpg"
	image_path.parent.mkdir(parents=True, exist_ok=True)
	image_path.write_bytes(b"fake-jpeg")

	response = client.get("/api/chat/content/images/docs/doc123/images/feed.jpg")

	assert response.status_code == 200
	assert response.headers["content-type"].startswith("image/jpeg")
	assert response.content == b"fake-jpeg"

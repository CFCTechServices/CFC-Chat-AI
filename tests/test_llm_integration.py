"""
Tests for the GPT-4o-mini LLM integration in ChatService.

These tests use unittest.mock to patch the OpenAI client so that no real
API calls are made.  They verify:
  - The correct model name is forwarded to the OpenAI API
  - Conversation history is included in the messages array
  - A RuntimeError is raised when OPENAI_API_KEY is absent
  - Gemini-related settings are no longer present on the Settings object
  - The no-LLM fallback (simple stub answer) still works
"""

from __future__ import annotations

import types
from unittest.mock import MagicMock, patch

import pytest

from app.config import settings


# ---------------------------------------------------------------------------
# Settings sanity checks
# ---------------------------------------------------------------------------

def test_default_openai_model_is_gpt4o_mini():
    """OPENAI_MODEL must default to gpt-4o-mini after the migration."""
    assert settings.OPENAI_MODEL == "gpt-4o-mini"


def test_gemini_settings_removed():
    """GEMINI_API_KEY and GEMINI_MODEL must no longer exist on Settings."""
    assert not hasattr(settings, "GEMINI_API_KEY"), (
        "GEMINI_API_KEY should have been removed from Settings"
    )
    assert not hasattr(settings, "GEMINI_MODEL"), (
        "GEMINI_MODEL should have been removed from Settings"
    )


# ---------------------------------------------------------------------------
# _generate_llm_answer — OpenAI path
# ---------------------------------------------------------------------------

def _make_openai_response(text: str):
    """Build a minimal fake openai.chat.completions.create response."""
    msg = MagicMock()
    msg.content = text
    choice = MagicMock()
    choice.message = msg
    resp = MagicMock()
    resp.choices = [choice]
    return resp


@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_uses_gpt4o_mini(mock_init, mock_settings):
    """_generate_llm_answer must call OpenAI with model=gpt-4o-mini."""
    mock_settings.OPENAI_API_KEY = "sk-test-key"
    mock_settings.OPENAI_MODEL = "gpt-4o-mini"

    fake_response = _make_openai_response("Test answer. [CHUNKS_CITED: chunk-1]")

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with patch("openai.OpenAI") as mock_openai_cls:
        mock_client = MagicMock()
        mock_openai_cls.return_value = mock_client
        mock_client.chat.completions.create.return_value = fake_response

        answer, _ = service._generate_llm_answer(
            question="What is CFC?",
            formatted_context="[CHUNK_ID: chunk-1]\nSome context.",
        )

    create_call = mock_client.chat.completions.create.call_args
    assert create_call is not None, "chat.completions.create was not called"
    assert create_call.kwargs.get("model") == "gpt-4o-mini", (
        f"Expected model=gpt-4o-mini, got {create_call.kwargs.get('model')}"
    )
    assert "Test answer" in answer


@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_includes_conversation_history(mock_init, mock_settings):
    """Conversation history messages must appear before the current question."""
    mock_settings.OPENAI_API_KEY = "sk-test-key"
    mock_settings.OPENAI_MODEL = "gpt-4o-mini"

    fake_response = _make_openai_response("Answer. [CHUNKS_CITED: chunk-1]")
    history = [
        {"role": "user", "content": "Hi"},
        {"role": "assistant", "content": "Hello!"},
    ]

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with patch("openai.OpenAI") as mock_openai_cls:
        mock_client = MagicMock()
        mock_openai_cls.return_value = mock_client
        mock_client.chat.completions.create.return_value = fake_response

        service._generate_llm_answer(
            question="Follow-up question?",
            formatted_context="",
            conversation_history=history,
        )

    messages_sent = mock_client.chat.completions.create.call_args.kwargs["messages"]
    roles = [m["role"] for m in messages_sent]

    # system + user(Hi) + assistant(Hello!) + user(current question)
    assert roles[0] == "system"
    assert roles[1] == "user"    # "Hi"
    assert roles[2] == "assistant"  # "Hello!"
    assert roles[-1] == "user"   # current question


@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_raises_when_no_api_key(mock_init, mock_settings):
    """A RuntimeError should be raised when OPENAI_API_KEY is missing."""
    mock_settings.OPENAI_API_KEY = None  # No key set

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with pytest.raises(RuntimeError, match="No OpenAI API key configured"):
        service._generate_llm_answer(
            question="Any question?",
            formatted_context="",
        )


# ---------------------------------------------------------------------------
# ask_question — stub fallback when no key is configured
# ---------------------------------------------------------------------------

@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_ask_question_uses_simple_stub_without_api_key(mock_init, mock_settings):
    """When OPENAI_API_KEY is absent, ask_question falls back gracefully."""
    mock_settings.OPENAI_API_KEY = None
    mock_settings.DEFAULT_TOP_K = 3

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    # Stub out the internal helpers that require actual infrastructure
    service._is_vector_store_empty = MagicMock(return_value=False)
    service.document_rag_pipeline = MagicMock()
    service.document_rag_pipeline.retrieve_context.return_value = [
        {
            "text": "CFC manufactures animal feed.",
            "source": "Brochure",
            "score": 0.9,
            "rank": 1,
            "chunk_id": "chunk-1",
            "image_paths": [],
            "section_title": "About CFC",
        }
    ]
    service.document_rag_pipeline.format_context.return_value = "CFC manufactures animal feed."
    service.video_vector_store = MagicMock()
    service._filter_and_rank_images = MagicMock(return_value=[])
    service._calculate_confidence = MagicMock(return_value=0.9)
    service._extract_primary_video_reference = MagicMock(return_value={})

    result = service.ask_question("What does CFC do?")

    assert result["success"] is True
    assert result["answer"]  # non-empty stub answer
    assert "CFC manufactures animal feed" in result["answer"]


# ---------------------------------------------------------------------------
# Ensure google.generativeai is NOT imported by chat_service
# ---------------------------------------------------------------------------

def test_google_generativeai_not_imported():
    """After migration, google.generativeai must not be importable from chat_service."""
    import sys
    import importlib

    # Re-import to get a fresh module reference
    import app.services.chat_service as cs_module

    # The module's globals should not have 'genai' or 'google' bound at the top level
    assert "genai" not in vars(cs_module), (
        "'genai' is still present in chat_service module namespace"
    )

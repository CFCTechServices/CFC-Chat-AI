"""
Tests for the Azure OpenAI GPT-4o-mini integration in ChatService.

These tests use unittest.mock to patch the AzureOpenAI client so that no real
API calls are made.  They verify:
  - Azure settings (API key, endpoint, deployment, version) are present on Settings
  - Standard OpenAI / Gemini settings are no longer present
  - _generate_llm_answer uses AzureOpenAI with the correct credentials
  - Conversation history is correctly included in the messages array
  - A RuntimeError is raised when Azure credentials are missing
  - The no-LLM fallback (simple stub answer) still works
  - google.generativeai is no longer imported
"""

from __future__ import annotations

from unittest.mock import MagicMock, patch

import pytest

from app.config import settings


# ---------------------------------------------------------------------------
# Settings sanity checks
# ---------------------------------------------------------------------------

def test_azure_settings_present():
    """All required Azure OpenAI settings must be defined on Settings."""
    assert hasattr(settings, "AZURE_OPENAI_API_KEY"), "AZURE_OPENAI_API_KEY missing from Settings"
    assert hasattr(settings, "AZURE_OPENAI_ENDPOINT"), "AZURE_OPENAI_ENDPOINT missing from Settings"
    assert hasattr(settings, "AZURE_OPENAI_DEPLOYMENT"), "AZURE_OPENAI_DEPLOYMENT missing from Settings"
    assert hasattr(settings, "AZURE_OPENAI_API_VERSION"), "AZURE_OPENAI_API_VERSION missing from Settings"
    assert settings.AZURE_OPENAI_API_VERSION == "2024-08-01-preview"


def test_openai_and_gemini_settings_removed():
    """Standard OPENAI_API_KEY, OPENAI_MODEL, and Gemini settings must all be gone."""
    for attr in ("OPENAI_API_KEY", "OPENAI_MODEL", "GEMINI_API_KEY", "GEMINI_MODEL"):
        assert not hasattr(settings, attr), f"{attr} should have been removed from Settings"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_azure_response(text: str):
    """Build a minimal fake AzureOpenAI chat.completions.create response."""
    msg = MagicMock()
    msg.content = text
    choice = MagicMock()
    choice.message = msg
    resp = MagicMock()
    resp.choices = [choice]
    return resp


# ---------------------------------------------------------------------------
# _generate_llm_answer — Azure path
# ---------------------------------------------------------------------------

@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_uses_azure_client(mock_init, mock_settings):
    """_generate_llm_answer must create an AzureOpenAI client with the correct credentials."""
    mock_settings.AZURE_OPENAI_API_KEY = "azure-key"
    mock_settings.AZURE_OPENAI_ENDPOINT = "https://my-resource.openai.azure.com/"
    mock_settings.AZURE_OPENAI_DEPLOYMENT = "gpt-4o-mini"
    mock_settings.AZURE_OPENAI_API_VERSION = "2024-08-01-preview"

    fake_response = _make_azure_response("Test answer. [CHUNKS_CITED: chunk-1]")

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with patch("openai.AzureOpenAI") as mock_azure_cls:
        mock_client = MagicMock()
        mock_azure_cls.return_value = mock_client
        mock_client.chat.completions.create.return_value = fake_response

        answer, _ = service._generate_llm_answer(
            question="What is CFC?",
            formatted_context="[CHUNK_ID: chunk-1]\nSome context.",
        )

    # AzureOpenAI must have been constructed with the right credentials
    mock_azure_cls.assert_called_once()
    kwargs = mock_azure_cls.call_args.kwargs
    assert kwargs["api_key"] == "azure-key"
    assert kwargs["azure_endpoint"] == "https://my-resource.openai.azure.com/"
    assert kwargs["api_version"] == "2024-08-01-preview"

    # Deployment name must be passed as model
    create_call = mock_client.chat.completions.create.call_args
    assert create_call.kwargs.get("model") == "gpt-4o-mini"
    assert "Test answer" in answer


@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_includes_conversation_history(mock_init, mock_settings):
    """Conversation history messages must appear in the messages array before the current question."""
    mock_settings.AZURE_OPENAI_API_KEY = "azure-key"
    mock_settings.AZURE_OPENAI_ENDPOINT = "https://my-resource.openai.azure.com/"
    mock_settings.AZURE_OPENAI_DEPLOYMENT = "gpt-4o-mini"
    mock_settings.AZURE_OPENAI_API_VERSION = "2024-08-01-preview"

    fake_response = _make_azure_response("Answer. [CHUNKS_CITED: chunk-1]")
    history = [
        {"role": "user", "content": "Hi"},
        {"role": "assistant", "content": "Hello!"},
    ]

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with patch("openai.AzureOpenAI") as mock_azure_cls:
        mock_client = MagicMock()
        mock_azure_cls.return_value = mock_client
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
    assert roles[1] == "user"       # "Hi"
    assert roles[2] == "assistant"  # "Hello!"
    assert roles[-1] == "user"      # current question


@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_generate_llm_answer_raises_when_credentials_missing(mock_init, mock_settings):
    """A RuntimeError should be raised when Azure credentials are not configured."""
    mock_settings.AZURE_OPENAI_API_KEY = None
    mock_settings.AZURE_OPENAI_ENDPOINT = None

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

    with pytest.raises(RuntimeError, match="Azure OpenAI API key and endpoint must both be configured"):
        service._generate_llm_answer(
            question="Any question?",
            formatted_context="",
        )


# ---------------------------------------------------------------------------
# ask_question — stub fallback when azure credentials are absent
# ---------------------------------------------------------------------------

@patch("app.services.chat_service.settings")
@patch("app.services.chat_service.ChatService.__init__", return_value=None)
def test_ask_question_uses_simple_stub_without_azure_credentials(mock_init, mock_settings):
    """When Azure credentials are absent, ask_question falls back to the simple stub."""
    mock_settings.AZURE_OPENAI_API_KEY = None
    mock_settings.AZURE_OPENAI_ENDPOINT = None
    mock_settings.DEFAULT_TOP_K = 3

    from app.services.chat_service import ChatService

    service = ChatService.__new__(ChatService)

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
    assert result["answer"]
    assert "CFC manufactures animal feed" in result["answer"]


# ---------------------------------------------------------------------------
# Ensure google.generativeai is NOT imported by chat_service
# ---------------------------------------------------------------------------

def test_google_generativeai_not_imported():
    """After migration, google.generativeai must not be present in chat_service module namespace."""
    import app.services.chat_service as cs_module

    assert "genai" not in vars(cs_module), (
        "'genai' is still present in chat_service module namespace"
    )

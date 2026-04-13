from fastapi import FastAPI
from fastapi.testclient import TestClient
import pytest
from app.api.endpoints import health
from app.config import settings

@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(health.router)
    client = TestClient(app)
    return client

def test_health_endpoint(client):
    """Test that the health endpoint returns the expected JSON response."""
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "ok": True,
        "message": "CFC Animal Feed Software Chatbot API is running",
        "version": settings.API_VERSION,
    }
def test_health_detailed_endpoint(client):
    """Test that the health detailed endpoint returns the expected JSON response."""

    response = client.get("/health/detailed")

    assert response.status_code == 200
    data = response.json()
    assert isinstance(data["ok"], bool)
    assert "version" in data
    assert "message" in data
    assert "services" in data


def test_health_endpoint_post_method_not_allowed(client):
    """Test that POST on health endpoint is not allowed."""
    response = client.post("/health")

    assert response.status_code == 405
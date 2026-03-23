import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from app.api.endpoints import visibility


@pytest.fixture()
def client():
    app = FastAPI()
    app.include_router(visibility.router)
    return TestClient(app)


class FakeVectorStore:
    """Fake VectorStore that storing pinecone database for testing."""

    def __init__(self, stats = None, error = None, index_name = "t_index"):
        self._stats = stats or {}
        self._error = error
        self.index_name = index_name

    def get_index_stats(self):
        if self._error:
            raise self._error
        return self._stats


def patch_vector_store(monkeypatch, fake_vector_store):
    """Patch lazy import target used inside endpoints: app.core.vector_store."""
    monkeypatch.setattr(visibility, "vector_store", fake_vector_store)

def test_get_vector_store_stats_uses_total_vector_count_key(client, monkeypatch):
    """Test that total_vector_count key is used when present."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={
                "total_vector_count": 1000,
                "dimension": 1536,
                "indexFullness": 0.5,
                "namespaces": {
                    "ns1": {"vectorCount": 300},
                    "ns2": {"vectorCount": 700},
                },
            },
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["success"] is True
    assert data["index_name"] == "index"
    assert data["total_vectors"] == 1000
    assert data["dimension"] == 1536
    assert data["index_fullness"] == 0.5
    assert len(data["namespaces"]) == 2


def test_get_vector_store_stats_uses_totalVectorCount_key(client, monkeypatch):
    """Test that totalVectorCount key is used when present."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"totalVectorCount": 99, "namespaces": {}},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["success"] is True
    assert response.json()["total_vectors"] == 99


def test_get_vector_store_stats_zero_total_vector_count(client, monkeypatch):
    """Test that total_vector_count of 0 is respected and not treated as falsy.
    If the endpoint used `or` chaining, 0 would fall through to totalVectorCount (99),
    producing an incorrect result.
    """
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 0, "totalVectorCount": 99, "namespaces": {}},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["total_vectors"] == 0


def test_get_vector_store_stats_empty_stats_dict(client, monkeypatch):
    """Test that an completely empty stats dict returns safe zero/None defaults."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(stats={}, index_name="index"),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["success"] is True
    assert data["total_vectors"] == 0
    assert data["namespaces"] == []
    assert data["dimension"] is None
    assert data["index_fullness"] is None


def test_get_vector_store_stats_namespaces_none(client, monkeypatch):
    """Test that an explicit None value for namespaces is handled safely."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 500, "namespaces": None},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["total_vectors"] == 500
    assert data["namespaces"] == []


def test_get_vector_store_stats_sums_namespaces_when_no_total_key(client, monkeypatch):
    """Test that total_vectors is computed by summing namespace vectorCounts when total keys are absent."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"namespaces": {"ns1": {"vectorCount": 400}, "ns2": {"vectorCount": 600}}},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["success"] is True
    assert response.json()["total_vectors"] == 1000


def test_get_vector_store_stats_empty_namespaces(client, monkeypatch):
    """Test that an empty namespaces dict returns an empty list."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 0, "namespaces": {}},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["namespaces"] == []
    assert data["total_vectors"] == 0


def test_get_vector_store_stats_empty_namespace_name_defaults_to_default(client, monkeypatch):
    """Test that an empty namespace name is returned as 'default'."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 1000, "namespaces": {"": {"vectorCount": 5}}},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    namespaces = response.json()["namespaces"]
    assert len(namespaces) == 1
    assert namespaces[0]["name"] == "default"
    assert namespaces[0]["vector_count"] == 5


def test_get_vector_store_stats_optional_fields_are_none_when_absent(client, monkeypatch):
    """Test that dimension and index_fullness are None """
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(stats={"total_vector_count": 1}, index_name="index"),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["dimension"] is None
    assert data["index_fullness"] is None


def test_get_vector_store_stats_uses_index_fullness_snake_case(client, monkeypatch):
    """Test that index_fullness (snake_case) is used when camelCase indexFullness is absent."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 1000, "index_fullness": 0.75},
            index_name="index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["success"] is True
    assert response.json()["index_fullness"] == 0.75

def test_get_vector_store_stats_returns_500_on_exception(client, monkeypatch):
    """Test that a 500 status code is returned when Pinecone is unavailable (API_44)."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(error=ConnectionError("Failed to connect to Pinecone index"), index_name="index"),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 500
    assert response.json() == {"detail": "Failed to fetch vector store stats"}

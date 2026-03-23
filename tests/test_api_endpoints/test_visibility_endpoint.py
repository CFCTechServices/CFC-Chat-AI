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
    """Fake VectorStore that bypasses Pinecone initialisation."""

    def __init__(self, stats=None, error=None, index_name="test-index"):
        self._stats = stats or {}
        self._error = error
        self.index_name = index_name

    def get_index_stats(self):
        if self._error:
            raise self._error
        return self._stats


def patch_vector_store(monkeypatch, fake):
    """Replace the module-level vector_store instance used by the endpoint."""
    monkeypatch.setattr(visibility, "vector_store", fake)


# ---------------------------------------------------------------------------
# Success cases
# ---------------------------------------------------------------------------

def test_get_vector_store_stats_uses_total_vector_count_key(client, monkeypatch):
    """total_vector_count key is preferred when present in stats."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={
                "total_vector_count": 42,
                "dimension": 1536,
                "indexFullness": 0.5,
                "namespaces": {
                    "ns1": {"vectorCount": 20},
                    "ns2": {"vectorCount": 22},
                },
            },
            index_name="my-index",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["success"] is True
    assert data["index_name"] == "my-index"
    assert data["total_vectors"] == 42
    assert data["dimension"] == 1536
    assert data["index_fullness"] == 0.5
    assert len(data["namespaces"]) == 2


def test_get_vector_store_stats_uses_totalVectorCount_key(client, monkeypatch):
    """Falls back to totalVectorCount when total_vector_count is absent."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"totalVectorCount": 99, "namespaces": {}},
            index_name="idx",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["total_vectors"] == 99


def test_get_vector_store_stats_sums_namespaces_when_no_total_key(client, monkeypatch):
    """total_vectors is computed by summing namespace vectorCounts when total keys are absent."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"namespaces": {"a": {"vectorCount": 10}, "b": {"vectorCount": 30}}},
            index_name="idx",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["total_vectors"] == 40


def test_get_vector_store_stats_empty_namespaces(client, monkeypatch):
    """Empty namespaces dict is handled gracefully and returns an empty list."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 0, "namespaces": {}},
            index_name="idx",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["namespaces"] == []
    assert data["total_vectors"] == 0


def test_get_vector_store_stats_empty_namespace_name_defaults_to_default(client, monkeypatch):
    """A namespace with an empty string key is mapped to the name 'default'."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 5, "namespaces": {"": {"vectorCount": 5}}},
            index_name="idx",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    namespaces = response.json()["namespaces"]
    assert len(namespaces) == 1
    assert namespaces[0]["name"] == "default"
    assert namespaces[0]["vector_count"] == 5


def test_get_vector_store_stats_optional_fields_are_none_when_absent(client, monkeypatch):
    """dimension and index_fullness are None when not present in stats."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(stats={"total_vector_count": 1}, index_name="idx"),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    data = response.json()
    assert data["dimension"] is None
    assert data["index_fullness"] is None


def test_get_vector_store_stats_uses_index_fullness_snake_case(client, monkeypatch):
    """index_fullness (snake_case) is used when camelCase indexFullness is absent."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(
            stats={"total_vector_count": 1, "index_fullness": 0.75},
            index_name="idx",
        ),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 200
    assert response.json()["index_fullness"] == 0.75


# ---------------------------------------------------------------------------
# Error cases
# ---------------------------------------------------------------------------

def test_get_vector_store_stats_returns_500_on_exception(client, monkeypatch):
    """An unexpected exception from get_index_stats returns HTTP 500."""
    patch_vector_store(
        monkeypatch,
        FakeVectorStore(error=RuntimeError("pinecone unavailable"), index_name="idx"),
    )

    response = client.get("/visibility/vector-store")

    assert response.status_code == 500
    assert response.json()["detail"] == "Failed to fetch vector store stats"

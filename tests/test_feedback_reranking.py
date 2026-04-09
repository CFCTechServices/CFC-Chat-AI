"""
Integration tests: Feedback-driven RAG re-ranking (Phase 1 + Phase 2)

These tests hit real Pinecone and Supabase — they require:
  • A populated Pinecone index (PINECONE_INDEX_NAME in .env)
  • Migrations 001 and 003 applied to Supabase
  • At least one row in profiles (for Phase 2 temp message creation)

Run with:
    pytest tests/test_feedback_reranking.py -v
"""

import math
import pytest

from app.config import settings
from app.core.rag import RAGPipeline
from app.core.embeddings import EmbeddingModel
from app.core.feedback_service import FeedbackService
from app.core.supabase_service import supabase

TEST_QUERY       = "What are the nutritional requirements for cattle feed?"
SIMILAR_QUERY    = "How much protein should be in cattle feed?"
DISSIMILAR_QUERY = "How do I reset my password?"
POSITIVE_N       = 2
NEGATIVE_N       = 1


# ── Helpers ───────────────────────────────────────────────────────────────────

def cosine_sim(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na  = math.sqrt(sum(x * x for x in a))
    nb  = math.sqrt(sum(x * x for x in b))
    return dot / (na * nb) if na and nb else 0.0


# ── Session-scoped fixtures (created once per test run) ───────────────────────

@pytest.fixture(scope="module")
def rag():
    return RAGPipeline()


@pytest.fixture(scope="module")
def embedding_model():
    return EmbeddingModel()


@pytest.fixture(scope="module")
def feedback_svc():
    return FeedbackService()


@pytest.fixture(scope="module")
def baseline_chunks(rag):
    """RAG results with feedback disabled — pure Pinecone order."""
    settings.FEEDBACK_ENABLED = False
    chunks = rag.retrieve_context(TEST_QUERY, top_k=5)
    settings.FEEDBACK_ENABLED = True
    return chunks


@pytest.fixture(scope="module")
def all_chunk_ids(baseline_chunks):
    return [c["chunk_id"] for c in baseline_chunks if c.get("chunk_id")]


@pytest.fixture(scope="module")
def verified_chunk_ids(all_chunk_ids):
    """Subset of all_chunk_ids that exist in document_chunks (FK-safe)."""
    if not all_chunk_ids:
        return []
    res = (
        supabase.table("document_chunks")
        .select("chunk_id")
        .in_("chunk_id", all_chunk_ids)
        .execute()
    )
    found = {r["chunk_id"] for r in (res.data or [])}
    return [cid for cid in all_chunk_ids if cid in found]


# ── Function-scoped fixtures (set up and torn down per test) ──────────────────

@pytest.fixture
def phase1_injected(verified_chunk_ids):
    """
    Upsert test rows into chunk_feedback_scores:
      first POSITIVE_N chunks → net +3
      last  NEGATIVE_N chunks → net -3
    Yields the injected chunk_ids. Cleans up after the test.
    """
    if not verified_chunk_ids:
        pytest.skip("No verified chunk_ids — check Pinecone/Supabase sync")

    rows = []
    for i, cid in enumerate(verified_chunk_ids):
        if i < POSITIVE_N:
            net = 3
        elif i >= len(verified_chunk_ids) - NEGATIVE_N:
            net = -3
        else:
            continue
        rows.append({
            "chunk_id":       cid,
            "positive_count": max(0, net),
            "negative_count": max(0, -net),
        })

    injected_ids = [r["chunk_id"] for r in rows]
    supabase.table("chunk_feedback_scores").upsert(rows, on_conflict="chunk_id").execute()

    settings.FEEDBACK_ENABLED = True
    yield injected_ids

    supabase.table("chunk_feedback_scores").delete().in_("chunk_id", injected_ids).execute()
    settings.FEEDBACK_ENABLED = True


@pytest.fixture
def phase2_message_id():
    """
    Yield a real chat_messages UUID for Phase 2 FK constraint.
    Uses an existing row if available, otherwise creates a temporary
    session + message and cleans up after the test.
    """
    res = supabase.table("chat_messages").select("id").limit(1).execute()
    if res.data:
        yield res.data[0]["id"]
        return

    prof = supabase.table("profiles").select("id").limit(1).execute()
    if not prof.data:
        pytest.skip("profiles table is empty — cannot create temp message for Phase 2")

    user_id = prof.data[0]["id"]
    sess = supabase.table("chat_sessions").insert({
        "user_id": user_id,
        "title":   "__test_phase2__",
    }).execute()
    session_id = sess.data[0]["id"]

    msg = supabase.table("chat_messages").insert({
        "session_id": session_id,
        "role":       "assistant",
        "content":    "__test__",
    }).execute()
    message_id = msg.data[0]["id"]

    yield message_id

    supabase.table("chat_messages").delete().eq("id", message_id).execute()
    supabase.table("chat_sessions").delete().eq("id", session_id).execute()


@pytest.fixture
def phase2_injected(verified_chunk_ids, embedding_model, phase2_message_id):
    """
    Encode TEST_QUERY and insert rows into chunk_feedback_events (rating=+1).
    Yields {"seed_embedding", "chunk_ids"} for use in test assertions.
    Cleans up injected rows after the test.
    """
    chunk_ids = verified_chunk_ids[:POSITIVE_N]
    if not chunk_ids:
        pytest.skip("No verified chunk_ids for Phase 2 injection")

    seed_emb = embedding_model.encode_query(TEST_QUERY)
    rows = [
        {
            "chunk_id":        cid,
            "message_id":      phase2_message_id,
            "rating":          1,
            "query_embedding": seed_emb,
        }
        for cid in chunk_ids
    ]
    res = supabase.table("chunk_feedback_events").insert(rows).execute()
    row_ids = [r["id"] for r in (res.data or [])]

    settings.FEEDBACK_ENABLED = True
    yield {"seed_embedding": seed_emb, "chunk_ids": chunk_ids}

    if row_ids:
        supabase.table("chunk_feedback_events").delete().in_("id", row_ids).execute()
    settings.FEEDBACK_ENABLED = True


# ── Tests ─────────────────────────────────────────────────────────────────────

class TestBaseline:
    def test_returns_chunks(self, baseline_chunks):
        assert len(baseline_chunks) > 0, (
            "RAG returned no chunks — check PINECONE_INDEX_NAME and that the index has data"
        )

    def test_chunks_have_required_fields(self, baseline_chunks):
        for chunk in baseline_chunks:
            assert chunk.get("chunk_id"), f"chunk missing chunk_id: {chunk}"
            assert chunk.get("score") is not None, f"chunk missing score: {chunk}"
            assert chunk.get("rank") is not None, f"chunk missing rank: {chunk}"


class TestPhase1:
    def test_positive_chunks_adjusted_score_is_higher(self, baseline_chunks, phase1_injected, rag):
        baseline_scores = {c["chunk_id"]: c["score"] for c in baseline_chunks}
        reranked = rag.retrieve_context(TEST_QUERY, top_k=5)

        positive_ids = set(phase1_injected[:POSITIVE_N])
        for chunk in reranked:
            cid = chunk.get("chunk_id")
            if cid not in positive_ids:
                continue
            adj   = chunk.get("adjusted_score")
            score = baseline_scores.get(cid)
            assert adj is not None, f"chunk {cid} has no adjusted_score"
            assert adj > score, (
                f"positive chunk {cid}: adjusted ({adj:.4f}) should be > baseline ({score:.4f})"
            )

    def test_negative_chunks_adjusted_score_is_lower(self, baseline_chunks, phase1_injected, rag):
        baseline_scores = {c["chunk_id"]: c["score"] for c in baseline_chunks}
        reranked = rag.retrieve_context(TEST_QUERY, top_k=5)

        negative_ids = set(phase1_injected[-NEGATIVE_N:])
        for chunk in reranked:
            cid = chunk.get("chunk_id")
            if cid not in negative_ids:
                continue
            adj   = chunk.get("adjusted_score")
            score = baseline_scores.get(cid)
            assert adj is not None, f"chunk {cid} has no adjusted_score"
            assert adj < score, (
                f"negative chunk {cid}: adjusted ({adj:.4f}) should be < baseline ({score:.4f})"
            )


class TestPhase2Similarity:
    def test_similar_query_meets_threshold(self, embedding_model):
        seed    = embedding_model.encode_query(TEST_QUERY)
        similar = embedding_model.encode_query(SIMILAR_QUERY)
        sim     = cosine_sim(seed, similar)
        assert sim >= settings.FEEDBACK_SIM_THRESHOLD, (
            f"Similar query cosine similarity {sim:.4f} is below threshold "
            f"{settings.FEEDBACK_SIM_THRESHOLD} — adjust FEEDBACK_SIM_THRESHOLD"
        )

    def test_dissimilar_query_below_threshold(self, embedding_model):
        seed       = embedding_model.encode_query(TEST_QUERY)
        dissimilar = embedding_model.encode_query(DISSIMILAR_QUERY)
        sim        = cosine_sim(seed, dissimilar)
        assert sim < settings.FEEDBACK_SIM_THRESHOLD, (
            f"Dissimilar query cosine similarity {sim:.4f} unexpectedly exceeds threshold "
            f"{settings.FEEDBACK_SIM_THRESHOLD}"
        )


class TestPhase2Boosting:
    def test_similar_query_returns_weighted_scores(
        self, feedback_svc, embedding_model, all_chunk_ids, phase2_injected
    ):
        similar_emb = embedding_model.encode_query(SIMILAR_QUERY)
        scores = feedback_svc.get_query_aware_scores(
            similar_emb, all_chunk_ids, settings.FEEDBACK_SIM_THRESHOLD
        )
        assert scores, (
            "Expected query-aware scores for similar query but got none. "
            f"Check FEEDBACK_SIM_THRESHOLD ({settings.FEEDBACK_SIM_THRESHOLD})"
        )
        for cid in phase2_injected["chunk_ids"]:
            assert cid in scores, f"Injected chunk {cid} not present in query-aware scores"
            assert scores[cid] > 0, f"Expected positive weighted score for {cid}, got {scores[cid]}"

    def test_dissimilar_query_returns_no_scores(
        self, feedback_svc, embedding_model, all_chunk_ids, phase2_injected
    ):
        # Confirm events are in DB, then prove a dissimilar query still gets nothing
        assert phase2_injected["chunk_ids"], "Phase 2 events must be injected before this test"
        dissimilar_emb = embedding_model.encode_query(DISSIMILAR_QUERY)
        scores = feedback_svc.get_query_aware_scores(
            dissimilar_emb, all_chunk_ids, settings.FEEDBACK_SIM_THRESHOLD
        )
        assert not scores, (
            f"Dissimilar query should return no scores but got: {scores}"
        )


class TestCombinedReranking:
    def test_boosted_chunks_have_higher_adjusted_score(self, rag, phase2_injected):
        reranked = rag.retrieve_context(SIMILAR_QUERY, top_k=5)
        boosted_ids = set(phase2_injected["chunk_ids"])

        for chunk in reranked:
            if chunk.get("chunk_id") not in boosted_ids:
                continue
            adj   = chunk.get("adjusted_score")
            score = chunk.get("score")
            assert adj is not None, f"Boosted chunk {chunk['chunk_id']} has no adjusted_score"
            assert adj > score, (
                f"Boosted chunk {chunk['chunk_id']}: adjusted ({adj:.4f}) should be > "
                f"pinecone score ({score:.4f})"
            )

    def test_all_returned_chunks_have_adjusted_score(self, rag, phase2_injected):
        # phase2_injected ensures at least one chunk has a query-aware signal,
        # triggering the full re-ranking path for all returned chunks
        assert phase2_injected["chunk_ids"], "Phase 2 events must be injected before this test"
        reranked = rag.retrieve_context(TEST_QUERY, top_k=5)
        for chunk in reranked:
            assert chunk.get("adjusted_score") is not None, (
                f"chunk {chunk.get('chunk_id')} missing adjusted_score in combined re-ranking"
            )

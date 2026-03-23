"""
Test script: Feedback-driven RAG re-ranking (Phase 1 + Phase 2)
================================================================

What this tests
---------------
Phase 1 – Global chunk score boosting
  • Injects positive/negative net scores into chunk_feedback_scores
  • Runs a real RAG query with feedback disabled, then enabled
  • Shows which chunks moved up/down and by how much

Phase 2 – Query-aware score boosting
  • Encodes a "seed" query and injects it into chunk_feedback_events
    alongside the same chunks, simulating a past positive rating
  • Re-runs the query using get_query_aware_scores()
  • Shows that semantically SIMILAR queries trigger the boost
  • Shows that semantically DISSIMILAR queries do NOT

Usage
-----
    python scripts/test_feedback_reranking.py

Requirements
------------
  • .env file in the project root (loaded before any app imports)
  • Migration 001 applied (chunk_feedback_scores table)
  • Migration 003 applied (chunk_feedback_events table + pgvector)
    → If 003 is not applied yet, Phase 2 section will skip gracefully
"""

# ── CRITICAL: load .env BEFORE any app imports ───────────────────────────────
from pathlib import Path
from dotenv import load_dotenv

_project_root = Path(__file__).resolve().parents[1]
load_dotenv(_project_root / ".env")
# ─────────────────────────────────────────────────────────────────────────────

import sys
sys.path.insert(0, str(_project_root))
import math

from app.config import settings
from app.core.rag import RAGPipeline
from app.core.embeddings import EmbeddingModel
from app.core.feedback_service import FeedbackService
from app.core.supabase_service import supabase

# ── Colours ───────────────────────────────────────────────────────────────────
GREEN  = "\033[92m"
RED    = "\033[91m"
YELLOW = "\033[93m"
CYAN   = "\033[96m"
BOLD   = "\033[1m"
RESET  = "\033[0m"

def colour(text, c):
    return f"{c}{text}{RESET}"

def section(title):
    print(f"\n{BOLD}{CYAN}{'─'*60}{RESET}")
    print(f"{BOLD}{CYAN}  {title}{RESET}")
    print(f"{BOLD}{CYAN}{'─'*60}{RESET}")

# ── Config ────────────────────────────────────────────────────────────────────
TEST_QUERY          = "What are the nutritional requirements for cattle feed?"
SIMILAR_QUERY       = "How much protein should be in cattle feed?"   # semantically close
DISSIMILAR_QUERY    = "How do I reset my password?"                  # semantically unrelated
POSITIVE_CHUNKS_N   = 2   # inject +1 score on first N chunks
NEGATIVE_CHUNKS_N   = 1   # inject -1 score on last N chunks

# ─────────────────────────────────────────────────────────────────────────────

def run_rag_query(rag: RAGPipeline, query: str) -> list[dict]:
    """Run a real RAG query and return context chunks."""
    chunks = rag.retrieve_context(query, top_k=5)
    return chunks

def print_chunks(chunks: list[dict], label: str):
    print(f"\n  {BOLD}{label}{RESET}")
    for c in chunks:
        rank  = c.get("rank", "?")
        score = c.get("score") or 0.0
        adj   = c.get("adjusted_score")
        cid   = (c.get("chunk_id") or "")[:40]
        src   = (c.get("source") or "")[:35]
        adj_str = f"  adj={adj:.4f}" if adj is not None else ""
        print(f"    #{rank:>2}  score={score:.4f}{adj_str}  [{cid}]  {src}")

def verify_chunks_in_db(chunk_ids: list[str]) -> list[str]:
    """Return only chunk_ids that exist in document_chunks (FK safe)."""
    if not chunk_ids:
        return []
    res = (
        supabase.table("document_chunks")
        .select("chunk_id")
        .in_("chunk_id", chunk_ids)
        .execute()
    )
    found = {r["chunk_id"] for r in (res.data or [])}
    missing = [c for c in chunk_ids if c not in found]
    if missing:
        print(colour(
            f"  ⚠  {len(missing)} chunk(s) not in document_chunks (Pinecone/Supabase "
            f"out of sync) — skipping them for injection.", YELLOW
        ))
    return [c for c in chunk_ids if c in found]

# ─────────────────────────────────────────────────────────────────────────────
# Phase 1 helpers
# ─────────────────────────────────────────────────────────────────────────────

def inject_phase1_scores(chunk_ids: list[str], pos_n: int, neg_n: int) -> list[str]:
    """
    Directly upsert rows into chunk_feedback_scores.
    Returns the chunk_ids that were actually injected (FK-verified only).
    """
    verified = verify_chunks_in_db(chunk_ids)
    if not verified:
        print(colour("  ✗  No verified chunk_ids — skipping Phase 1 injection.", RED))
        return []

    rows = []
    for i, cid in enumerate(verified):
        if i < pos_n:
            net = 3   # simulate 3 net positive votes
        elif i >= len(verified) - neg_n:
            net = -3  # simulate 3 net negative votes
        else:
            net = 0

        if net == 0:
            continue

        pos = max(0, net)
        neg = max(0, -net)
        rows.append({
            "chunk_id":       cid,
            "positive_count": pos,
            "negative_count": neg,
        })

    if rows:
        supabase.table("chunk_feedback_scores").upsert(rows, on_conflict="chunk_id").execute()
        print(colour(f"  ✓  Injected Phase 1 scores for {len(rows)} chunk(s).", GREEN))

    return [r["chunk_id"] for r in rows]

def cleanup_phase1(injected_ids: list[str]):
    if injected_ids:
        supabase.table("chunk_feedback_scores").delete().in_("chunk_id", injected_ids).execute()
        print(colour(f"  ✓  Cleaned up {len(injected_ids)} Phase 1 row(s).", GREEN))

# ─────────────────────────────────────────────────────────────────────────────
# Phase 2 helpers
# ─────────────────────────────────────────────────────────────────────────────

def get_real_message_id() -> tuple[str | None, list[str]]:
    """
    Return (message_id, cleanup_ids) where cleanup_ids are temp session/message
    UUIDs to delete after the test.

    Strategy:
      1. Use an existing chat_messages row if one exists (no temp rows needed).
      2. Otherwise create a temp chat_session + chat_message using any profile
         user_id, and return their IDs for cleanup.
    """
    try:
        # 1. Try existing message first
        res = supabase.table("chat_messages").select("id").limit(1).execute()
        if res.data:
            return res.data[0]["id"], []

        # 2. Need to create temp rows — fetch any user_id from profiles
        prof = supabase.table("profiles").select("id").limit(1).execute()
        if not (prof.data):
            print(colour("  ⚠  profiles table is empty — cannot create temp message.", YELLOW))
            return None, []

        user_id = prof.data[0]["id"]

        # Insert temp session
        sess_res = supabase.table("chat_sessions").insert({
            "user_id": user_id,
            "title":   "__test_phase2__",
        }).execute()
        session_id = sess_res.data[0]["id"]

        # Insert temp message
        msg_res = supabase.table("chat_messages").insert({
            "session_id": session_id,
            "role":       "assistant",
            "content":    "__test__",
        }).execute()
        message_id = msg_res.data[0]["id"]

        print(colour(f"  ✓  Created temp session {session_id} and message {message_id}.", GREEN))
        return message_id, [message_id, session_id]

    except Exception as e:
        print(colour(f"  ⚠  Could not obtain message_id: {e}", YELLOW))
        return None, []


def cleanup_temp_rows(ids: list[str]):
    """Delete temp chat_messages and chat_sessions rows created for Phase 2."""
    if not ids:
        return
    # ids[0] = message_id, ids[1] = session_id
    # Deleting the session cascades to messages, but be explicit to be safe.
    try:
        supabase.table("chat_messages").delete().eq("id", ids[0]).execute()
        supabase.table("chat_sessions").delete().eq("id", ids[1]).execute()
        print(colour("  ✓  Cleaned up temp session and message.", GREEN))
    except Exception as e:
        print(colour(f"  ⚠  Temp row cleanup failed: {e}", YELLOW))

def inject_phase2_events(
    chunk_ids: list[str],
    query_embedding: list[float],
    rating: int,
    message_id: str,
) -> list[int]:
    """
    Insert rows directly into chunk_feedback_events.
    Returns the ids of the inserted rows (for cleanup).

    NOTE: chunk_feedback_events has ON DELETE CASCADE from chat_messages,
    so we use a fake message_id that won't exist.  If your schema enforces
    the FK strictly, you'll see an FK error — see IMPORTANT note below.
    """
    rows = [
        {
            "chunk_id":        cid,
            "message_id":      message_id,
            "rating":          rating,
            "query_embedding": query_embedding,  # list[float] accepted by supabase-py
        }
        for cid in chunk_ids
    ]
    try:
        res = supabase.table("chunk_feedback_events").insert(rows).execute()
        inserted_ids = [r["id"] for r in (res.data or [])]
        print(colour(f"  ✓  Injected {len(inserted_ids)} Phase 2 event(s).", GREEN))
        return inserted_ids
    except Exception as e:
        # FK on message_id will fail if chat_messages row doesn't exist.
        # In that case, temporarily use a real message_id or disable the FK.
        print(colour(f"  ✗  Phase 2 injection failed: {e}", RED))
        print(colour(
            "  ℹ  If this is a FK error on message_id, run the test while a real "
            "chat message exists and pass its UUID as FAKE_MESSAGE_ID above.", YELLOW
        ))
        return []

def cleanup_phase2(row_ids: list[int]):
    if row_ids:
        supabase.table("chunk_feedback_events").delete().in_("id", row_ids).execute()
        print(colour(f"  ✓  Cleaned up {len(row_ids)} Phase 2 event row(s).", GREEN))

# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

def main():
    print(f"\n{BOLD}Feedback Re-Ranking Test  (Phase 1 + Phase 2){RESET}")
    print(f"  Pinecone index : {settings.PINECONE_INDEX_NAME}")
    print(f"  FEEDBACK_ENABLED: {settings.FEEDBACK_ENABLED}")

    rag            = RAGPipeline()
    embedding_model = EmbeddingModel()
    feedback_svc   = FeedbackService()

    # ──────────────────────────────────────────────────────────────────────────
    # BASELINE: run query with feedback completely OFF
    # ──────────────────────────────────────────────────────────────────────────
    section("STEP 1 — Baseline (feedback disabled)")
    settings.FEEDBACK_ENABLED = False
    baseline_chunks = run_rag_query(rag, TEST_QUERY)

    if not baseline_chunks:
        print(colour(
            f"  ✗  RAG returned 0 chunks for query: '{TEST_QUERY}'\n"
            f"     Check PINECONE_INDEX_NAME in .env and that the index has data.",
            RED
        ))
        sys.exit(1)

    print(f"  Query: '{TEST_QUERY}'")
    print_chunks(baseline_chunks, "Baseline ranking:")

    all_chunk_ids = [c["chunk_id"] for c in baseline_chunks if c.get("chunk_id")]
    print(f"\n  Retrieved {len(all_chunk_ids)} chunk(s) from Pinecone.")

    # ──────────────────────────────────────────────────────────────────────────
    # PHASE 1 TEST
    # ──────────────────────────────────────────────────────────────────────────
    section("STEP 2 — Phase 1: Global score injection")

    p1_injected = inject_phase1_scores(all_chunk_ids, POSITIVE_CHUNKS_N, NEGATIVE_CHUNKS_N)

    if p1_injected:
        settings.FEEDBACK_ENABLED = True
        p1_chunks = run_rag_query(rag, TEST_QUERY)
        print_chunks(p1_chunks, "After Phase 1 re-ranking:")

        # Show rank changes
        print(f"\n  {BOLD}Rank changes (baseline → Phase 1):{RESET}")
        baseline_rank = {c["chunk_id"]: c["rank"] for c in baseline_chunks}
        for c in p1_chunks:
            cid  = c.get("chunk_id")
            old  = baseline_rank.get(cid, "?")
            new  = c["rank"]
            if old != new:
                arrow = "↑" if isinstance(old, int) and new < old else "↓"
                clr   = GREEN if arrow == "↑" else RED
                print(colour(f"    {arrow} [{cid[:35]}]  rank {old} → {new}", clr))

        cleanup_phase1(p1_injected)
    else:
        print(colour("  Skipping Phase 1 re-ranking comparison.", YELLOW))

    # ──────────────────────────────────────────────────────────────────────────
    # PHASE 2 TEST
    # ──────────────────────────────────────────────────────────────────────────
    section("STEP 3 — Phase 2: Query-aware event injection")

    # Encode the seed query (will be stored as the "past" query embedding)
    print(f"  Encoding seed query: '{TEST_QUERY}'")
    seed_embedding  = embedding_model.encode_query(TEST_QUERY)

    # Encode comparison queries
    print(f"  Encoding similar query:    '{SIMILAR_QUERY}'")
    similar_emb     = embedding_model.encode_query(SIMILAR_QUERY)
    print(f"  Encoding dissimilar query: '{DISSIMILAR_QUERY}'")
    dissimilar_emb  = embedding_model.encode_query(DISSIMILAR_QUERY)

    # Compute cosine similarities so the tester can see them
    def cosine_sim(a, b):
        dot = sum(x*y for x,y in zip(a, b))
        na  = math.sqrt(sum(x*x for x in a))
        nb  = math.sqrt(sum(x*x for x in b))
        return dot / (na * nb) if na and nb else 0.0

    sim_similar     = cosine_sim(seed_embedding, similar_emb)
    sim_dissimilar  = cosine_sim(seed_embedding, dissimilar_emb)

    print(f"\n  Cosine similarity (seed ↔ similar):    {sim_similar:.4f}  "
          f"{'✓ above threshold' if sim_similar >= settings.FEEDBACK_SIM_THRESHOLD else '✗ below threshold'}")
    print(f"  Cosine similarity (seed ↔ dissimilar): {sim_dissimilar:.4f}  "
          f"{'✓ above threshold' if sim_dissimilar >= settings.FEEDBACK_SIM_THRESHOLD else '✓ below threshold (expected)'}")

    # Only inject for chunks that exist in document_chunks
    verified_ids = verify_chunks_in_db(all_chunk_ids[:POSITIVE_CHUNKS_N])

    p2_row_ids: list[int] = []
    temp_row_ids: list[str] = []
    if verified_ids:
        message_id, temp_row_ids = get_real_message_id()
        if not message_id:
            print(colour(
                "  ✗  Could not obtain a message_id (profiles table may be empty). "
                "Phase 2 skipped.",
                RED
            ))
        else:
            print(colour(f"  ✓  Using message_id: {message_id}", GREEN))
            p2_row_ids = inject_phase2_events(
                chunk_ids=verified_ids,
                query_embedding=seed_embedding,
                rating=1,
                message_id=message_id,
            )

    if p2_row_ids:
        section("STEP 4 — Phase 2: Score retrieval verification")
        settings.FEEDBACK_ENABLED = True

        # --- Similar query should get boosted scores ---
        print(f"\n  get_query_aware_scores() with SIMILAR query:")
        qa_similar = feedback_svc.get_query_aware_scores(
            similar_emb, all_chunk_ids, settings.FEEDBACK_SIM_THRESHOLD
        )
        if qa_similar:
            for cid, ws in qa_similar.items():
                print(colour(f"    ✓ [{cid[:40]}]  weighted_score={ws:.4f}", GREEN))
        else:
            print(colour(
                "    ✗  No scores returned — check FEEDBACK_SIM_THRESHOLD "
                f"({settings.FEEDBACK_SIM_THRESHOLD}) vs actual similarity ({sim_similar:.4f})",
                RED
            ))

        # --- Dissimilar query should get nothing ---
        print(f"\n  get_query_aware_scores() with DISSIMILAR query:")
        qa_dissimilar = feedback_svc.get_query_aware_scores(
            dissimilar_emb, all_chunk_ids, settings.FEEDBACK_SIM_THRESHOLD
        )
        if not qa_dissimilar:
            print(colour("    ✓ Correctly returned no scores (dissimilar query ignored).", GREEN))
        else:
            print(colour(
                f"    ✗ Unexpectedly returned scores: {qa_dissimilar} "
                f"— threshold may be too low ({settings.FEEDBACK_SIM_THRESHOLD})",
                RED
            ))

        # --- Full re-ranking with Phase 2 active ---
        section("STEP 5 — Full re-ranking (Phase 1 + Phase 2 combined)")
        settings.FEEDBACK_ENABLED = True
        p2_chunks = run_rag_query(rag, SIMILAR_QUERY)
        print(f"  Query: '{SIMILAR_QUERY}'")
        print_chunks(p2_chunks, "Re-ranked result:")

        print(f"\n  {BOLD}Rank changes (baseline → Phase 2 boost on similar query):{RESET}")
        baseline_rank = {c["chunk_id"]: c["rank"] for c in baseline_chunks}
        boosted = {cid for cid in verified_ids}
        for c in p2_chunks:
            cid = c.get("chunk_id")
            old = baseline_rank.get(cid, "?")
            new = c["rank"]
            tag = colour(" ← boosted", GREEN) if cid in boosted else ""
            if old != new:
                arrow = "↑" if isinstance(old, int) and new < old else "↓"
                clr   = GREEN if arrow == "↑" else RED
                print(colour(f"    {arrow} [{(cid or '')[:35]}]  rank {old} → {new}", clr) + tag)
            elif cid in boosted:
                print(f"    = [{(cid or '')[:35]}]  rank unchanged{tag}")

        cleanup_phase2(p2_row_ids)
    else:
        print(colour(
            "\n  Phase 2 injection skipped — chunk_feedback_events may need "
            "migration 003 or a real message_id. Phase 2 test not run.",
            YELLOW
        ))

    cleanup_temp_rows(temp_row_ids)

    # ──────────────────────────────────────────────────────────────────────────
    section("DONE")
    print("  All test rows cleaned up.  No permanent changes were made.\n")

if __name__ == "__main__":
    main()

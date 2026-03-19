"""
Test script: verifies that feedback scores nudge RAG chunk rankings.

What it does:
  1. Runs a real RAG query and prints the initial Pinecone-ranked results.
  2. Injects a positive score (+3 net) for the LAST-ranked chunk and a
     negative score (-3 net) for the FIRST-ranked chunk directly into
     chunk_feedback_scores.
  3. Reruns the same query and prints the re-ranked results.
  4. Shows a side-by-side diff so you can see which chunks moved.
  5. Cleans up the injected scores so your data is not affected.

Usage:
    cd /path/to/your/project
    python scripts/test_feedback_reranking.py "your test question here"

    # or use the default question:
    python scripts/test_feedback_reranking.py
"""

import sys
import os
from pathlib import Path

# ── Load .env BEFORE any app imports so os.getenv() picks up the right values
# (app.config instantiates settings at import time, so .env must be loaded first)
from dotenv import load_dotenv
load_dotenv(Path(__file__).resolve().parents[1] / ".env")

# ── Make sure the app is importable from the project root ──
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.core.rag import RAGPipeline
from app.core.supabase_service import supabase

# ── Helpers ──
RESET  = "\033[0m"
BOLD   = "\033[1m"
GREEN  = "\033[32m"
RED    = "\033[31m"
YELLOW = "\033[33m"
CYAN   = "\033[36m"

def _short(text: str, n: int = 80) -> str:
    text = (text or "").replace("\n", " ").strip()
    return text[:n] + "…" if len(text) > n else text

def _print_chunks(chunks: list, label: str, highlight: dict = None) -> None:
    print(f"\n{BOLD}{label}{RESET}")
    print("─" * 90)
    for c in chunks:
        cid   = c.get("chunk_id", "?")
        rank  = c.get("rank", "?")
        score = c.get("score") or 0.0
        adj   = c.get("adjusted_score")
        text  = _short(c.get("text", ""), 70)

        score_str = f"{score:.4f}"
        adj_str   = f"  →  adjusted: {adj:.4f}" if adj is not None else ""

        color = ""
        tag   = ""
        if highlight:
            if cid == highlight.get("boosted"):
                color = GREEN
                tag   = "  ⬆ BOOSTED"
            elif cid == highlight.get("penalised"):
                color = RED
                tag   = "  ⬇ PENALISED"

        print(f"{color}  Rank {rank:>2}  score={score_str}{adj_str}  [{cid[:20]}…]{tag}")
        print(f"           {CYAN}{text}{RESET}{color}{RESET}")
    print("─" * 90)


def inject_score(chunk_id: str, positive: int, negative: int) -> None:
    """Directly upsert a row into chunk_feedback_scores for testing."""
    supabase.table("chunk_feedback_scores").upsert(
        {
            "chunk_id":       chunk_id,
            "positive_count": positive,
            "negative_count": negative,
        },
        on_conflict="chunk_id",
    ).execute()


def cleanup_scores(chunk_ids: list) -> None:
    """Remove the injected test rows."""
    supabase.table("chunk_feedback_scores").delete().in_("chunk_id", chunk_ids).execute()


def run_test(query: str) -> None:
    from app.config import settings

    rag = RAGPipeline()

    print(f"\n{BOLD}Query:{RESET} {CYAN}{query}{RESET}")

    # ── Step 1: retrieve WITHOUT feedback ──
    # Temporarily disable via the same flag that controls production behaviour
    settings.FEEDBACK_ENABLED = False
    before = rag.retrieve_context(query)
    settings.FEEDBACK_ENABLED = True  # restore

    if not before:
        print(f"{RED}No chunks returned. Check that your Pinecone index has data.{RESET}")
        return

    _print_chunks(before, "BEFORE feedback (pure Pinecone order)")

    # ── Step 2: verify chunk_ids exist in Supabase (FK check) ──
    # Pinecone and document_chunks can drift; we can only inject scores for
    # chunks that actually exist in Supabase due to the FK constraint.
    pinecone_ids = [c["chunk_id"] for c in before if c.get("chunk_id")]
    verified_res = supabase.table("document_chunks")\
        .select("chunk_id")\
        .in_("chunk_id", pinecone_ids)\
        .execute()
    verified_ids = [r["chunk_id"] for r in (verified_res.data or [])]

    unverified = set(pinecone_ids) - set(verified_ids)
    if unverified:
        print(f"\n{YELLOW}Warning: {len(unverified)} chunk(s) from Pinecone are not in "
              f"document_chunks — Pinecone and Supabase may be out of sync.{RESET}")
        print(f"  These chunk_ids cannot receive feedback scores (FK constraint):")
        for cid in unverified:
            print(f"    {cid}")

    if len(verified_ids) < 2:
        print(f"\n{RED}Not enough verified chunk_ids to run the boost/penalise test "
              f"(need at least 2 chunks present in both Pinecone and document_chunks).{RESET}")
        print(f"Re-ingest your documents to bring Pinecone and Supabase back in sync.")
        return

    # Pick from verified ids only
    verified_chunks = [c for c in before if c.get("chunk_id") in verified_ids]
    first_chunk = verified_chunks[0]["chunk_id"]   # will be penalised
    last_chunk  = verified_chunks[-1]["chunk_id"]  # will be boosted
    

    print(f"\n{YELLOW}Injecting test scores:{RESET}")
    print(f"  {RED}Penalising{RESET} rank-{verified_chunks[0]['rank']} chunk "
          f"[{first_chunk[:30]}…]  → net_score = -3")
    print(f"  {GREEN}Boosting{RESET}   rank-{verified_chunks[-1]['rank']} chunk "
          f"[{last_chunk[:30]}…]  → net_score = +3")

    inject_score(first_chunk, positive=0, negative=3)
    inject_score(last_chunk,  positive=3, negative=0)

    # ── Step 3: retrieve WITH feedback ──
    after = rag.retrieve_context(query)

    _print_chunks(
        after,
        "AFTER feedback (re-ranked)",
        highlight={"boosted": last_chunk, "penalised": first_chunk},
    )

    # ── Step 4: summary ──
    before_ranks = {c["chunk_id"]: c["rank"] for c in before}
    after_ranks  = {c["chunk_id"]: c["rank"] for c in after}

    print(f"\n{BOLD}Rank changes:{RESET}")
    moved = False
    for cid, before_rank in before_ranks.items():
        after_rank = after_ranks.get(cid, "?")
        if before_rank != after_rank:
            arrow = "⬆" if after_rank < before_rank else "⬇"
            color = GREEN if after_rank < before_rank else RED
            print(f"  {color}{arrow} [{cid[:30]}…]  {before_rank} → {after_rank}{RESET}")
            moved = True

    if not moved:
        print(f"  {YELLOW}No rank changes detected. "
              f"The injected net_score of ±3 may not be large enough to "
              f"overcome the score gap between chunks. Try a query with "
              f"more evenly-scored results, or increase the injected scores.{RESET}")

    # ── Step 5: cleanup ──
    cleanup_scores([first_chunk, last_chunk])
    print(f"\n{BOLD}Cleanup:{RESET} injected scores removed. Your data is unchanged.\n")


if __name__ == "__main__":
    query = " ".join(sys.argv[1:]) if len(sys.argv) > 1 else "how do I get started"
    run_test(query)

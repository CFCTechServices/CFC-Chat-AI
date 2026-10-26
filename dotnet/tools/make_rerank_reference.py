"""
Record Python FeedbackService.rerank() results for hand-built cases, as the answer key for
the C# FeedbackMath parity tests. Pure math: no Pinecone/Supabase calls are made
(importing the service needs .env only because it creates the Supabase client).

Usage (from the dotnet/ folder, repo .venv active):
    python tools\\make_rerank_reference.py
"""
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from dotenv import load_dotenv  # noqa: E402
load_dotenv(REPO_ROOT / ".env")

from app.core.feedback_service import FeedbackService  # noqa: E402

DEFAULTS = {"alpha": 0.3, "scale": 5.0, "alpha_query": 0.15, "scale_query": 3.0}
THREE = [("a", 0.80), ("b", 0.70), ("c", 0.60)]

CASES = [
    {"name": "no_feedback",                     "chunks": THREE, "global": {},                 "query": {}},
    {"name": "upvotes_lift_lower_chunk",        "chunks": THREE, "global": {"c": 5},           "query": {}},
    {"name": "downvotes_demote_top_chunk",      "chunks": THREE, "global": {"a": -4},          "query": {}},
    {"name": "query_aware_only",                "chunks": THREE, "global": {},                 "query": {"b": 1.8}},
    {"name": "both_signals_and_negative_query", "chunks": THREE, "global": {"a": 2, "c": -1},  "query": {"a": -1.5, "b": 2.4}},
    {"name": "large_vote_count_saturates",      "chunks": [("a", 0.80), ("b", 0.50)], "global": {"b": 100}, "query": {}},
    {"name": "ties_keep_original_order",        "chunks": [("a", 0.70), ("b", 0.70), ("c", 0.70)], "global": {}, "query": {}},
    {"name": "missing_chunk_id",                "chunks": [(None, 0.90), ("b", 0.50)], "global": {"b": 3}, "query": {}},
    {"name": "scale_zero_disables_global",      "chunks": [("a", 0.80), ("b", 0.70)], "global": {"b": 10}, "query": {},
     "settings": {"scale": 0.0}},
]

records = []
for case in CASES:
    s = {**DEFAULTS, **case.get("settings", {})}
    chunks = [{"chunk_id": cid, "score": score, "rank": i + 1}
              for i, (cid, score) in enumerate(case["chunks"])]

    result = FeedbackService.rerank(
        chunks,
        feedback_scores=case["global"],
        query_aware_scores=case["query"],
        alpha=s["alpha"], scale=s["scale"],
        alpha_query=s["alpha_query"], scale_query=s["scale_query"],
    )

    records.append({
        "name": case["name"],
        "settings": s,
        "chunks": [{"chunk_id": cid, "score": score} for cid, score in case["chunks"]],
        "global": case["global"],
        "query": case["query"],
        "expected": [{"chunk_id": c["chunk_id"], "rank": c["rank"], "adjusted_score": c["adjusted_score"]}
                     for c in result],
    })
    print(f"{case['name']:34} -> {[c['chunk_id'] for c in result]}")

out_path = (Path(__file__).resolve().parents[1] / "tests" / "CFC.ChatAI.Rag.Tests"
            / "TestData" / "feedback_rerank_reference.json")
out_path.write_text(json.dumps(records, indent=1), encoding="utf-8")
print(f"Wrote {len(records)} cases to {out_path}")
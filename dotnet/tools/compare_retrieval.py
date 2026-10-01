"""
Side-by-side retrieval comparison: Python vs C# embeddings against the live Pinecone index.

For each question: embeds the exact same text with the Python model and the C#
service, queries Pinecone with both (plus a repeat Python query to measure
Pinecone's own run-to-run variation), and prints chunk IDs with scores.
Read-only: only queries Pinecone, never writes.

Requirements:
    Repo's .venv active; C# service running (dotnet run --project tools/CFC.Rag.EmbeddingService)

Usage (from the repo root):
    python dotnet\\tools\\compare_retrieval.py
"""
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from dotenv import load_dotenv  # noqa: E402
load_dotenv(REPO_ROOT / ".env")  # must happen before app.config is imported
os.environ["EMBEDDING_BACKEND"] = "python"  # encode() uses the Python model

from app.core.embeddings import EmbeddingModel  # noqa: E402
from app.core.vector_store import VectorStore  # noqa: E402

TOP_K = 5
QUESTIONS = [
    "How do i add new feed ingredients?",
    "how do i change the amount of NEG DIARY",
    "How do I get started with Concept 5?",
    "How do I export data from CFC5 to another program?",
    "How do I set up Concept 5 on a network workstation?",
    "How do I create a ration for a horse?",
    "What is a nutrient ratio template?",
    "How do I find an equivalent ingredient?",
    "How do I browse and edit records in CFC5?",
    "How do I build a beef cattle ration?",
    "reset password",
    "What does DM% mean in the glossary?",
    "formual not saving after i chnage ingredient prices",
    "Can I set the mixer temperature to 72°F before adding the premix?",
    'My ration shows "infeasible" — what does that mean and how do I fix it?',
]


def top(store, vec):
    res = store.query(vector=vec, top_k=TOP_K, include_metadata=False)
    return [(m.get("id"), m.get("score")) for m in res.get("matches", [])]


def fmt(results):
    return "  ".join(f"{cid[:8]}({score:.6f})" for cid, score in results)


embedder = EmbeddingModel()
store = VectorStore()
summary = {"identical": 0, "same_set_reordered": 0, "different": 0}

for q in QUESTIONS:
    py_vec = embedder.encode([q])[0]
    cs_vec = EmbeddingModel._encode_csharp([q])[0]
    max_diff = max(abs(a - b) for a, b in zip(py_vec, cs_vec))

    py1, py2, cs = top(store, py_vec), top(store, py_vec), top(store, cs_vec)
    py_ids, py2_ids, cs_ids = [c for c, _ in py1], [c for c, _ in py2], [c for c, _ in cs]

    if cs_ids == py_ids:
        verdict = "IDENTICAL"
        summary["identical"] += 1
    elif set(cs_ids) == set(py_ids):
        verdict = "SAME SET, REORDERED"
        summary["same_set_reordered"] += 1
    else:
        verdict = "DIFFERENT"
        summary["different"] += 1

    print(f"\nQ: {q}")
    print(f"   max element diff (py vs c#): {max_diff:.2e}")
    print(f"   Python   : {fmt(py1)}")
    print(f"   Python 2 : {fmt(py2)}   {'(stable)' if py2_ids == py_ids else '(PINECONE VARIED)'}")
    print(f"   C#       : {fmt(cs)}")
    print(f"   -> {verdict}")

print(f"\nSummary over {len(QUESTIONS)} questions: {summary}")
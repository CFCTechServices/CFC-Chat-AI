"""
Record Pinecone retrieval results from the EXISTING Python VectorStore,
as the answer key for the C# PineconeVectorStore parity check.

For each question: embeds it with the Python model, queries Pinecone through the app's own
VectorStore (read-only), and saves the query vector plus the returned chunk IDs and scores to:
    tests/CFC.ChatAI.Pinecone.Tests/TestData/retrieval_reference.json

C# then replays the same query vectors, so only the vector store is being compared.
Note: results reflect the index at the time this runs. Re-run after documents are ingested.

Requirements:
- Repo's .venv active with requirements.txt installed
- .env with Pinecone credentials

Usage (from the dotnet/ folder):
    python tools\\make_retrieval_reference.py
"""
import json 
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from dotenv import load_dotenv #noqa: E402
load_dotenv(REPO_ROOT / ".env") # must happen before app.config is imported
os.environ["EMBEDDING_BACKEND"] = "python"

from app.core.embeddings import EmbeddingModel # noqa: E402
from app.core.vector_store import VectorStore # noqa: E402

TOP_K = 5  # Number of top results to retrieve for each query
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

embedder = EmbeddingModel()
store = VectorStore()
records = []

for q in QUESTIONS:
    vec = embedder.encode_query(q)
    res = store.query(vector=vec, top_k=TOP_K, include_metadata=False)
    matches = [{"id": m.get("id"), "score": float(m.get("score"))} for m in res.get("matches", [])]
    records.append({
        "question": q,
        "query_vector": vec,
        "matches": matches
    })
    print(f"{len(matches)} matches {q}")

out_path = Path(__file__).resolve().parents[1] / "tests" / "CFC.ChatAI.Pinecone.Tests" / "TestData" / "retrieval_reference.json"
out_path.write_text(json.dumps(records), encoding="utf-8")
print(f"Wrote {len(records)} questions to {out_path}")
"""
Record the full Python RAG pipeline output (retrieve_context + format_context) as the
answer key for the C# RagPipeline parity check (tools/CFC.ChatAI.Rag.PipelineCheck).

Feedback re-ranking is off so only retrieval, merge and formatting are compared.
This uses the same questions as retrieval_reference.json. Configure the archived Python
baseline with the matching non-production provider targets in .archive/.env first.

Usage (from the repository root with the legacy Python dependencies installed):
    python tools/make_pipeline_reference.py
"""
import json
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
LEGACY_ROOT = REPO_ROOT / ".archive"
sys.path.insert(0, str(LEGACY_ROOT))

from dotenv import load_dotenv  # noqa: E402
load_dotenv(LEGACY_ROOT / ".env")
os.environ["FEEDBACK_ENABLED"] = "false"  # must be set BEFORE app.config is imported

from app.config import settings  # noqa: E402
from app.core.rag import RAGPipeline  # noqa: E402

assert not settings.FEEDBACK_ENABLED, "Feedback re-ranking must be off for this comparison"

TOP_K = 5
FIELDS = ["rank", "score", "chunk_id", "text", "source", "source_type",
          "doc_id", "section_id", "section_title"]

test_data = REPO_ROOT / "tests" / "CFC.ChatAI.Pinecone.Tests" / "TestData"
questions = [r["question"] for r in json.loads(
    (test_data / "retrieval_reference.json").read_text(encoding="utf-8"))]

pipeline = RAGPipeline()
records = []

for q in questions:
    chunks = pipeline.retrieve_context(q, top_k=TOP_K)
    context = pipeline.format_context(chunks)
    records.append({
        "question": q,
        "chunks": [{field: chunk.get(field) for field in FIELDS} for chunk in chunks],
        "context": context,
    })
    print(f"{len(chunks)} chunks  {q}")

out_path = test_data / "pipeline_reference.json"
out_path.write_text(json.dumps(records, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"Wrote {len(records)} questions to {out_path}")
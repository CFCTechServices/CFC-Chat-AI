from fastapi import FastAPI
from pydantic import BaseModel
from typing import List, Optional
from pathlib import Path
import re
import numpy as np
import pinecone
import os
from pinecone import Pinecone, ServerlessSpec

from sentence_transformers import SentenceTransformer
from docx import Document

app = FastAPI(title="CFC Chatbot API")

# -------- Config --------
DATA_DIR = Path(__file__).parent / "data"
EMBED_MODEL_NAME = "sentence-transformers/all-MiniLM-L6-v2"  

# -------- Pinecone Init --------
pc = Pinecone(api_key="pcsk_3j7Aoy_LJZMcGrCP6sG1Qc3Ehm62yq4otuwQmC1uHXT8QgryCJF2khebMqk5DyoPXgpjDR")

index_name = "cfc-chatbot"

# create index if not exists
if index_name not in pc.list_indexes().names():
    pc.create_index(
        name=index_name,
        dimension=384,          # all-MiniLM-L6-v2
        metric="cosine",
        spec=ServerlessSpec(
            cloud="aws",        
            region="us-east-1"  
        )
    )

index = pc.Index(index_name)


# -------- Embedding Model --------
class VectorIndex:
    def __init__(self):
        self.model: Optional[SentenceTransformer] = None

    def load_model(self):
        if self.model is None:
            self.model = SentenceTransformer(EMBED_MODEL_NAME)

INDEX = VectorIndex()


# -------- Helpers --------
def read_docx(path: Path) -> str:
    """Extract plain text from a .docx file."""
    doc = Document(str(path))
    paras = [p.text.strip() for p in doc.paragraphs if p.text and p.text.strip()]
    return "\n".join(paras)


def split_into_chunks(text: str, chunk_size: int = 500, overlap: int = 100) -> List[str]:
    """Chunk text into overlapping segments."""
    text = re.sub(r"\n{3,}", "\n\n", text).strip()
    chunks = []
    start = 0
    while start < len(text):
        end = min(len(text), start + chunk_size)
        cut = text.rfind(".", start, end)
        if cut == -1 or cut <= start + 0.5 * chunk_size:
            cut = end
        chunks.append(text[start:cut].strip())
        start = max(cut - overlap, cut)
    return [c for c in chunks if c]


# -------- Request/Response Models --------
class IngestRequest(BaseModel):
    filename: Optional[str] = "sample_guide.docx"  #


class SearchRequest(BaseModel):
    query: str
    top_k: Optional[int] = 5


class AskRequest(BaseModel):
    question: str
    top_k: Optional[int] = 4


# -------- Endpoints --------
@app.get("/")
def root():
    return {"ok": True, "message": "CFC Chatbot API running"}


@app.post("/ingest")
def ingest(req: IngestRequest):
    path = (DATA_DIR / req.filename).resolve()
    if not path.exists():
        return {"ok": False, "error": f"File not found: {path}"}

    text = read_docx(path)
    chunks = split_into_chunks(text, chunk_size=600, overlap=120)
    if not chunks:
        return {"ok": False, "error": "No text extracted from DOCX."}

    INDEX.load_model()
    embeddings = INDEX.model.encode(chunks, show_progress_bar=True).tolist()

    # Upsert into Pinecone
    vectors = [
        (f"{req.filename}-{i}", embeddings[i],
         {"text": chunks[i], "source": req.filename})
        for i in range(len(chunks))
    ]
    index.upsert(vectors)

    return {
        "ok": True,
        "source": str(path),
        "num_chunks": len(chunks),
        "example_chunk": chunks[0][:240]
    }


@app.post("/search")
def search(req: SearchRequest):
    INDEX.load_model()
    q_emb = INDEX.model.encode([req.query])[0].tolist()

    result = index.query(vector=q_emb, top_k=req.top_k or 5, include_metadata=True)

    hits = [
        {
            "rank": i + 1,
            "score": match["score"],
            "text": match["metadata"]["text"],
            "source": match["metadata"]["source"]
        }
        for i, match in enumerate(result["matches"])
    ]
    return {"ok": True, "results": hits}


@app.post("/ask")
def ask(req: AskRequest):
    INDEX.load_model()
    q_emb = INDEX.model.encode([req.question])[0].tolist()

    result = index.query(vector=q_emb, top_k=req.top_k or 4, include_metadata=True)

    hits = [
        {
            "rank": i + 1,
            "score": match["score"],
            "text": match["metadata"]["text"],
            "source": match["metadata"]["source"]
        }
        for i, match in enumerate(result["matches"])
    ]

    context = "\n\n".join(h["text"] for h in hits)

    return {
        "ok": True,
        "question": req.question,
        "context_snippets": hits,
        "answer_stub": context[:1200]
    }

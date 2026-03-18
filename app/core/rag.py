from typing import List, Dict, Any, Optional
import logging

from app.config import settings
from app.core.embeddings import EmbeddingModel
from app.core.vector_store import VectorStore
from app.core.supabase_service import supabase

logger = logging.getLogger(__name__)


def _to_float(value: Any) -> float | None:
    """Best-effort float conversion that tolerates None/strings."""
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


class RAGPipeline:
    """Retrieval-Augmented Generation pipeline."""

    def __init__(
        self,
        vector_store: Optional[VectorStore] = None,
        embedding_model: Optional[EmbeddingModel] = None,
    ) -> None:
        self.vector_store = vector_store or VectorStore()
        self.embedding_model = embedding_model or EmbeddingModel()

    def retrieve_context(
        self,
        query: str,
        top_k: int = None,
        metadata_filter: Dict[str, Any] | None = None,
    ) -> List[Dict[str, Any]]:
        """Retrieve relevant context for a query."""
        if top_k is None:
            top_k = settings.DEFAULT_TOP_K

        try:
            query_embedding = self.embedding_model.encode_query(query)
            results = self.vector_store.query(
                vector=query_embedding,
                top_k=top_k,
                include_metadata=True,
                metadata_filter=metadata_filter,
            )

            matches = results.get("matches", [])

            # Collect chunk IDs from Pinecone results and fetch the actual text rows from Supabase
            chunk_ids = [m.get("id") for m in matches if m.get("id")]
            db_rows = {}
            try:
                if chunk_ids:
                    resp = supabase.table("document_chunks").select("*").in_("chunk_id", chunk_ids).execute()
                    rows = getattr(resp, "data", []) or []
                    db_rows = {r.get("chunk_id"): r for r in rows}
            except Exception:
                db_rows = {}

            context_chunks: List[Dict[str, Any]] = []
            for index, match in enumerate(matches, start=1):
                metadata = match.get("metadata", {}) or {}
                cid = match.get("id")
                db_row = db_rows.get(cid, {}) if cid else {}

                # Prefer text from Supabase row; fall back to Pinecone metadata if missing
                text = db_row.get("content") or metadata.get("content") or metadata.get("text", "")

                context_chunks.append({
                    "rank": index,
                    "score": match.get("score"),
                    "text": text,
                    "source": db_row.get("source") or metadata.get("source", ""),
                    "source_type": db_row.get("source_type") or metadata.get("source_type", "document"),
                    "chunk_id": cid,
                    "doc_id": db_row.get("doc_id") or metadata.get("doc_id"),
                    "section_id": db_row.get("section_id") or metadata.get("section_id"),
                    "section_title": db_row.get("section_title") or metadata.get("section_title"),
                    "section_path": db_row.get("section_path") or metadata.get("section_path"),
                    "image_paths": db_row.get("image_paths") or metadata.get("image_paths", []),
                    "block_ids": metadata.get("block_ids", []),
                    "start_seconds": _to_float(db_row.get("start_seconds") or metadata.get("start_seconds")),
                    "end_seconds": _to_float(db_row.get("end_seconds") or metadata.get("end_seconds")),
                    "video_url": db_row.get("video_url") or metadata.get("video_url"),
                    "txt_url": db_row.get("txt_url") or metadata.get("txt_url"),
                    "srt_url": db_row.get("srt_url") or metadata.get("srt_url"),
                    "vtt_url": db_row.get("vtt_url") or metadata.get("vtt_url"),
                })

            return context_chunks

        except Exception as exc:
            logger.error("Failed to retrieve context for query: %s", exc)
            raise

    def format_context(self, context_chunks: List[Dict[str, Any]], max_length: int = None) -> str:
        """Format context chunks into a single context string."""
        if max_length is None:
            max_length = settings.MAX_CONTEXT_LENGTH

        context_parts: List[str] = []
        total_length = 0

        for chunk in context_chunks:
            title_line = f"Title: {chunk['section_title']}\n" if chunk.get("section_title") else ""
            body = chunk.get("text") or ""
            chunk_text = f"Source: {chunk.get('source', '')}\n{title_line}{body}\n"

            if total_length + len(chunk_text) > max_length:
                break

            context_parts.append(chunk_text)
            total_length += len(chunk_text)

        return "\n---\n".join(context_parts)

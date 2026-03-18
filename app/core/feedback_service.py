"""
Feedback-driven RAG re-ranking service (Phase 1).

Manages chunk feedback scores and applies them as a post-retrieval
re-ranking step.  Uses tanh to bound the multiplier so that feedback
nudges rankings without ever overriding semantic relevance.

Multiplier formula:
    multiplier = 1 + alpha * tanh(net_score / scale)

With the defaults (alpha=0.3, scale=5.0) the multiplier is bounded
to the range (0.7, 1.3) — a hard ±30% ceiling.
"""

from __future__ import annotations

import math
import logging
from typing import Dict, List, Optional

from app.core.supabase_service import supabase
from app.config import settings

logger = logging.getLogger(__name__)


class FeedbackService:
    """Manages chunk feedback scores and applies them to RAG re-ranking."""

    # ------------------------------------------------------------------
    # Score updates  (called from POST /chat/feedback)
    # ------------------------------------------------------------------

    def update_chunk_scores(
        self,
        chunk_ids: List[str],
        old_rating: Optional[int],
        new_rating: Optional[int],
    ) -> None:
        """
        Atomically adjust positive_count / negative_count for each chunk
        using the Postgres RPC function (no read-modify-write race).

        Parameters
        ----------
        chunk_ids : list of chunk IDs cited in the rated message
        old_rating : the rating *before* this change (+1, -1, or None)
        new_rating : the rating *after* this change (+1, -1, or None)
        """
        if not chunk_ids or old_rating == new_rating:
            return

        for chunk_id in chunk_ids:
            try:
                supabase.rpc(
                    "update_chunk_feedback_score",
                    {
                        "p_chunk_id": chunk_id,
                        "p_old_rating": old_rating,
                        "p_new_rating": new_rating,
                    },
                ).execute()
            except Exception as exc:
                logger.error(
                    "Failed to update feedback score for chunk %s: %s",
                    chunk_id,
                    exc,
                )

    # ------------------------------------------------------------------
    # Score retrieval  (called from RAGPipeline.retrieve_context)
    # ------------------------------------------------------------------

    def get_chunk_scores(self, chunk_ids: List[str]) -> Dict[str, int]:
        """
        Return {chunk_id: net_score} for a batch of chunks.
        Chunks with no feedback record are absent (treated as 0).
        """
        if not chunk_ids:
            return {}
        try:
            res = (
                supabase.table("chunk_feedback_scores")
                .select("chunk_id, net_score")
                .in_("chunk_id", chunk_ids)
                .execute()
            )
            return {
                row["chunk_id"]: row["net_score"]
                for row in (res.data or [])
            }
        except Exception as exc:
            logger.error("Failed to fetch chunk feedback scores: %s", exc)
            return {}  # fail-open: RAG continues without feedback

    # ------------------------------------------------------------------
    # Re-ranking  (called from RAGPipeline.retrieve_context)
    # ------------------------------------------------------------------

    @staticmethod
    def rerank(
        context_chunks: List[dict],
        feedback_scores: Dict[str, int],
        alpha: float | None = None,
        scale: float | None = None,
    ) -> List[dict]:
        """
        Apply feedback scores to re-rank context chunks using a
        tanh-bounded multiplier.

        Parameters
        ----------
        context_chunks : list of chunk dicts (must contain "score" and "chunk_id")
        feedback_scores : {chunk_id: net_score} from get_chunk_scores()
        alpha : maximum boost/penalty fraction (default from settings)
        scale : controls how quickly tanh saturates (default from settings)

        Returns
        -------
        A new list sorted by adjusted_score descending with updated ranks.
        """
        if alpha is None:
            alpha = settings.FEEDBACK_ALPHA
        if scale is None:
            scale = settings.FEEDBACK_SCALE

        for chunk in context_chunks:
            cid = chunk.get("chunk_id")
            net = feedback_scores.get(cid, 0)
            base = chunk.get("score") or 0.0

            # tanh bounds the output to (-1, 1), so the multiplier
            # is always in the range (1 - alpha, 1 + alpha).
            multiplier = 1 + alpha * math.tanh(net / scale)
            chunk["adjusted_score"] = base * multiplier

        reranked = sorted(
            context_chunks,
            key=lambda c: c.get("adjusted_score", 0),
            reverse=True,
        )

        for i, chunk in enumerate(reranked, start=1):
            chunk["rank"] = i

        return reranked

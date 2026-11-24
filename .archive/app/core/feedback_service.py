"""
Feedback-driven RAG re-ranking service (Phase 1 + Phase 2).

Phase 1 – Global chunk score boosting
--------------------------------------
Accumulates cumulative thumbs-up / thumbs-down votes per chunk and applies
them as a post-retrieval multiplier using a tanh-bounded formula so that
feedback nudges rankings without ever overriding semantic relevance.

    multiplier_global = 1 + α_global × tanh(net_score / scale_global)

With defaults (α=0.30, scale=5.0) the global multiplier is bounded to
(0.70, 1.30) — a hard ±30 % ceiling.

Phase 2 – Query-aware chunk score boosting
-------------------------------------------
Stores the query embedding with each feedback event.  At retrieval time a
pgvector cosine-similarity search finds past events whose query is semantically
close to the current one, and weighs their votes by that similarity.  This
means a chunk is only boosted when the *kind of question* being asked today
resembles the questions that generated positive feedback in the past.

    query_aware_signal = Σ (rating_i × similarity_i)  for similar past events
    multiplier_query   = α_query × tanh(query_aware_signal / scale_query)

Combined adjusted score:
    adjusted = pinecone_score × (multiplier_global + multiplier_query)
             = pinecone_score × (1
                 + α_global × tanh(net_score       / scale_global)
                 + α_query  × tanh(query_signal     / scale_query))

The total ceiling remains tight:
    (1 − α_global − α_query, 1 + α_global + α_query) = (0.55, 1.45) by default.
"""

from __future__ import annotations

import logging
import math
from typing import Dict, List, Optional

from app.core.supabase_service import supabase
from app.config import settings

logger = logging.getLogger(__name__)


class FeedbackService:
    """Manages chunk feedback scores and applies them to RAG re-ranking."""

    # ──────────────────────────────────────────────────────────────────────────
    # Phase 1 – Score updates  (called via Postgres RPC from chat.py)
    # ──────────────────────────────────────────────────────────────────────────

    def update_chunk_scores(
        self,
        chunk_ids: List[str],
        old_rating: Optional[int],
        new_rating: Optional[int],
    ) -> None:
        """
        Atomically adjust positive_count / negative_count for each chunk using
        the Postgres RPC function (no read-modify-write race).

        NOTE: In the current implementation this is called by the
        ``submit_message_feedback`` Postgres function directly, so Python
        never needs to invoke this method.  It is kept here for test scripts
        or future direct use.

        Parameters
        ----------
        chunk_ids   : list of chunk IDs cited in the rated message
        old_rating  : the rating *before* this change (+1, -1, or None)
        new_rating  : the rating *after*  this change (+1, -1, or None)
        """
        if not chunk_ids or old_rating == new_rating:
            return

        for chunk_id in chunk_ids:
            try:
                supabase.rpc(
                    "update_chunk_feedback_score",
                    {
                        "p_chunk_id":   chunk_id,
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

    # ──────────────────────────────────────────────────────────────────────────
    # Phase 1 – Global score retrieval  (called from RAGPipeline.retrieve_context)
    # ──────────────────────────────────────────────────────────────────────────

    def get_chunk_scores(self, chunk_ids: List[str]) -> Dict[str, int]:
        """
        Return {chunk_id: net_score} for a batch of chunks.
        Chunks with no feedback record are absent from the result (treated
        as net_score=0 by the caller).  Fails open — RAG continues even if
        this query errors.
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
            return {}  # fail-open

    # ──────────────────────────────────────────────────────────────────────────
    # Phase 2 – Query-aware event recording  (called from chat.py on feedback)
    # ──────────────────────────────────────────────────────────────────────────

    def record_feedback_events(
        self,
        chunk_ids: List[str],
        message_id: str,
        query_embedding: List[float],
        rating: int,
    ) -> None:
        """
        Insert one row into ``chunk_feedback_events`` for every cited chunk.

        Called from the feedback endpoint after a user submits a non-null
        rating.  The query_embedding is the 384-dim vector of the user's
        question (encoded in Python before calling this method because Postgres
        cannot run the sentence-transformer model).

        Parameters
        ----------
        chunk_ids       : chunk IDs cited in the assistant message being rated
        message_id      : UUID of the assistant message (for the FK)
        query_embedding : sentence-transformer vector for the user's question
        rating          : +1 (thumbs up) or -1 (thumbs down)
        """
        if not chunk_ids or not query_embedding:
            return

        rows = [
            {
                "chunk_id":        cid,
                "message_id":      message_id,
                "rating":          rating,
                # Supabase-py accepts a plain list for VECTOR columns
                "query_embedding": query_embedding,
            }
            for cid in chunk_ids
        ]

        try:
            supabase.table("chunk_feedback_events").insert(rows).execute()
            logger.info(
                "Recorded %d query-aware feedback event(s) for message %s",
                len(rows),
                message_id,
            )
        except Exception as exc:
            # Fail-open: Phase 2 events are best-effort; don't block the
            # feedback response if the events table isn't ready yet.
            logger.error(
                "Failed to record feedback events for message %s: %s",
                message_id,
                exc,
            )

    # ──────────────────────────────────────────────────────────────────────────
    # Phase 2 – Query-aware score retrieval  (called from RAGPipeline)
    # ──────────────────────────────────────────────────────────────────────────

    def get_query_aware_scores(
        self,
        query_embedding: List[float],
        chunk_ids: List[str],
        sim_threshold: Optional[float] = None,
    ) -> Dict[str, float]:
        """
        Call the ``get_query_aware_chunk_scores`` Postgres RPC and return
        {chunk_id: weighted_score} for chunks that have qualifying past events.

        The weighted_score is the sum of (rating × cosine_similarity) for all
        past feedback events whose stored query is at least ``sim_threshold``
        similar to the current query.

        Fails open — returns {} if the table or RPC doesn't exist yet.
        """
        if not chunk_ids or not query_embedding:
            return {}

        if sim_threshold is None:
            sim_threshold = settings.FEEDBACK_SIM_THRESHOLD

        try:
            res = supabase.rpc(
                "get_query_aware_chunk_scores",
                {
                    "p_query_emb":     query_embedding,
                    "p_chunk_ids":     chunk_ids,
                    "p_sim_threshold": sim_threshold,
                },
            ).execute()

            return {
                row["chunk_id"]: float(row["weighted_score"])
                for row in (res.data or [])
            }
        except Exception as exc:
            # Fail-open: Phase 2 degrades gracefully if not yet deployed.
            logger.warning(
                "get_query_aware_chunk_scores RPC failed (Phase 2 not "
                "deployed yet?): %s",
                exc,
            )
            return {}

    # ──────────────────────────────────────────────────────────────────────────
    # Re-ranking  (called from RAGPipeline.retrieve_context)
    # ──────────────────────────────────────────────────────────────────────────

    @staticmethod
    def rerank(
        context_chunks: List[dict],
        feedback_scores: Dict[str, int],
        query_aware_scores: Optional[Dict[str, float]] = None,
        alpha: Optional[float] = None,
        scale: Optional[float] = None,
        alpha_query: Optional[float] = None,
        scale_query: Optional[float] = None,
    ) -> List[dict]:
        """
        Apply Phase 1 + Phase 2 feedback signals to re-rank context chunks.

        Combined formula
        ----------------
        adjusted = pinecone_score
                   × (1
                      + α_global × tanh(net_score      / scale_global)   ← Phase 1
                      + α_query  × tanh(query_weighted / scale_query))   ← Phase 2

        Both tanh terms are independently bounded to (−α, +α), so the total
        multiplier range is (1 − α_global − α_query, 1 + α_global + α_query).
        With defaults (0.30, 0.15) that is (0.55, 1.45).

        Parameters
        ----------
        context_chunks     : list of chunk dicts (must contain "score" and "chunk_id")
        feedback_scores    : {chunk_id: net_score}   from get_chunk_scores()
        query_aware_scores : {chunk_id: weighted}    from get_query_aware_scores()
                             Pass None / {} to use Phase 1 only.
        alpha              : Phase 1 max boost fraction   (default from settings)
        scale              : Phase 1 tanh saturation scale (default from settings)
        alpha_query        : Phase 2 max boost fraction   (default from settings)
        scale_query        : Phase 2 tanh saturation scale (default from settings)

        Returns
        -------
        A new list sorted by adjusted_score descending with updated integer ranks.
        """
        if alpha is None:
            alpha = settings.FEEDBACK_ALPHA
        if scale is None:
            scale = settings.FEEDBACK_SCALE
        if alpha_query is None:
            alpha_query = settings.FEEDBACK_ALPHA_QUERY
        if scale_query is None:
            scale_query = settings.FEEDBACK_SCALE_QUERY

        qa_scores: Dict[str, float] = query_aware_scores or {}

        for chunk in context_chunks:
            cid  = chunk.get("chunk_id")
            base = chunk.get("score") or 0.0

            # Phase 1: global vote signal
            global_net = feedback_scores.get(cid, 0) if cid else 0
            global_boost = alpha * math.tanh(global_net / scale) if scale else 0.0

            # Phase 2: query-aware signal (0 if Phase 2 not deployed)
            query_weighted = qa_scores.get(cid, 0.0) if cid else 0.0
            query_boost = (
                alpha_query * math.tanh(query_weighted / scale_query)
                if scale_query and query_weighted != 0.0
                else 0.0
            )

            chunk["adjusted_score"] = base * (1.0 + global_boost + query_boost)

        reranked = sorted(
            context_chunks,
            key=lambda c: c.get("adjusted_score", 0.0),
            reverse=True,
        )

        for i, chunk in enumerate(reranked, start=1):
            chunk["rank"] = i

        return reranked

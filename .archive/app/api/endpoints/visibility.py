import logging
from fastapi import APIRouter, HTTPException
from app.api.models.responses import VectorStoreStatsResponse, NamespaceStats
from app.core.vector_store import VectorStore

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/visibility", tags=["visibility"])

# Initialize vector store once per process
vector_store = VectorStore()


@router.get("/vector-store", response_model=VectorStoreStatsResponse)
async def get_vector_store_stats():
    """Expose basic Pinecone vector store statistics for visibility."""
    try:
        stats = vector_store.get_index_stats()
        # Be careful to handle 0 explicitly instead of using 'or' chaining
        total_vector_count = stats.get("total_vector_count")
        total_vector_count_camel = stats.get("totalVectorCount")
        
        if total_vector_count is not None:
            total_vectors = total_vector_count
        elif total_vector_count_camel is not None:
            total_vectors = total_vector_count_camel
        else:
            total_vectors = sum(
                namespace.get("vectorCount", 0)
                for namespace in (stats.get("namespaces") or {}).values()
            )

        namespaces = [
            NamespaceStats(
                name=namespace_name or "default",
                vector_count=namespace_stats.get("vectorCount", 0)
            )
            for namespace_name, namespace_stats in (stats.get("namespaces") or {}).items()
        ]

        return VectorStoreStatsResponse(
            success=True,
            index_name=vector_store.index_name,
            total_vectors=total_vectors,
            dimension=stats.get("dimension"),
            index_fullness=stats.get("indexFullness") or stats.get("index_fullness"),
            namespaces=namespaces
        )
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"Failed to fetch vector store stats: {exc}")
        raise HTTPException(status_code=500, detail="Failed to fetch vector store stats")

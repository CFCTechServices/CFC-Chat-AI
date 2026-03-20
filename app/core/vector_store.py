from typing import List, Dict, Any, Optional
from pinecone import Pinecone, ServerlessSpec
import logging
from app.config import settings

logger = logging.getLogger(__name__)

class VectorStore:
    """Pinecone vector store management."""
    
    def __init__(self, index_name: Optional[str] = None, namespace: Optional[str] = None):
        self.pc = Pinecone(api_key=settings.PINECONE_API_KEY)
        self.index_name = index_name or settings.PINECONE_INDEX_NAME
        self.namespace = namespace if namespace is not None else getattr(settings, "PINECONE_NAMESPACE", None)
        self.index = None
        self._initialize_index()
    
    def _initialize_index(self):
        """Initialize Pinecone index."""
        try:
            # Create index if it doesn't exist
            if self.index_name not in self.pc.list_indexes().names():
                self.pc.create_index(
                    name=self.index_name,
                    dimension=settings.EMBED_DIMENSION,
                    metric="cosine",
                    spec=ServerlessSpec(
                        cloud=settings.PINECONE_CLOUD,
                        region=settings.PINECONE_REGION
                    )
                )
                logger.info(f"Created new Pinecone index: {self.index_name}")
            
            self.index = self.pc.Index(self.index_name)
            logger.info(f"Connected to Pinecone index: {self.index_name}")
            
        except Exception as e:
            logger.error(f"Failed to initialize Pinecone index: {e}")
            raise
    
    def upsert_vectors(self, vectors: List[tuple]) -> Dict[str, Any]:
        """Upsert vectors to Pinecone index."""
        try:
            kwargs = {}
            if self.namespace:
                kwargs["namespace"] = self.namespace
            response = self.index.upsert(vectors=vectors, **kwargs)
            logger.info(f"Upserted {len(vectors)} vectors to index")
            return response
        except Exception as e:
            logger.error(f"Failed to upsert vectors: {e}")
            raise
    
    def query(
        self,
        vector: List[float],
        top_k: int = 5,
        include_metadata: bool = True,
        metadata_filter: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """Query similar vectors from Pinecone index."""
        try:
            kwargs = {
                "vector": vector,
                "top_k": top_k,
                "include_metadata": include_metadata,
            }
            if metadata_filter:
                kwargs["filter"] = metadata_filter
            if self.namespace:
                kwargs["namespace"] = self.namespace

            response = self.index.query(**kwargs)
            return response
        except Exception as e:
            logger.error(f"Failed to query vectors: {e}")
            raise
    
    def delete_document(self, chunk_ids: List[str]) -> None:
        """Delete vectors by their chunk IDs."""
        if not chunk_ids:
            return
        try:
            kwargs = {}
            if self.namespace:
                kwargs["namespace"] = self.namespace
            self.index.delete(ids=chunk_ids, **kwargs)
            logger.info(f"Deleted {len(chunk_ids)} vectors from index")
        except Exception as e:
            logger.error(f"Failed to delete vectors: {e}")
            raise

    def delete_by_prefix(self, prefix: str):
        """Delete vectors with IDs starting with prefix."""
        try:
            logger.info(f"Deleting vectors with prefix: {prefix}")
        except Exception as e:
            logger.error(f"Failed to delete vectors with prefix {prefix}: {e}")
            raise
    
    def get_index_stats(self) -> Dict[str, Any]:
        """Get index statistics."""
        try:
            stats = self.index.describe_index_stats()
            return stats
        except Exception as e:
            logger.error(f"Failed to get index stats: {e}")
            raise

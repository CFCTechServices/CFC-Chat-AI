from typing import List, Optional
import os
import logging

import requests
from sentence_transformers import SentenceTransformer

from app.config import settings

logger = logging.getLogger(__name__)

#Migration test switch
# python default        - current behavior
# compare               - use python vectors, but also call the C# service and print parity
# csharp                - use vectors from the C# embeddings service

EMBEDDING_BACKEND = os.getenv("EMBEDDING_BACKEND", "python").lower()
EMBEDDING_SERVICE_URL = os.getenv("EMBEDDING_SERVICE_URL", "http://localhost:5100")

class EmbeddingModel:

    def __init__(self):
        self.model: Optional[SentenceTransformer] = None
        self.model_name = settings.EMBED_MODEL_NAME
    
    def load_model(self):
        if self.model is None:
            logger.info(f"Loading embedding model: {self.model_name}")

            try:
                self.model = SentenceTransformer(self.model_name, local_files_only=True)
            except Exception:
                # First time use - model not yet in local cache, allow the download
                self.model = SentenceTransformer(self.model_name)
            logger.info("Embedding model loaded successfully")
    
    def encode(self, texts: List[str], show_progress: bool = False) -> List[List[float]]:
        if EMBEDDING_BACKEND == "csharp":
            return self._encode_csharp(texts)
        
        if self.model is None:
            self.load_model()
        
        try: 
            embeddings = self.model.encode(texts, show_progress_bar = show_progress).tolist()
        except Exception as e:
            logger.error(f"Failed to encode texts: {e}")
            raise
        
        if EMBEDDING_BACKEND == "compare":
            try: 
                self._print_parity(texts, embeddings, self._encode_csharp(texts))
            except Exception as e:
                print(f"[embed-parity] C# service call failed: {e}", flush=True)
            
        return embeddings
        
    def encode_query(self, query: str) -> List[float]:
        return self.encode([query])[0]
        

    #---- Migration test helpers --- 
    @staticmethod
    def _encode_csharp(texts: List[str]) -> List[List[float]]:
        resp = requests.post(f"{EMBEDDING_SERVICE_URL}/embed", json={"texts": texts}, timeout=60)
        resp.raise_for_status()
        return resp.json()["embeddings"]
    
    @staticmethod
    def _print_parity(texts, py_vecs, cs_vecs) -> None:
        for text, a, b in zip(texts, py_vecs, cs_vecs):
            dot = sum(x * y for x, y in zip(a,b))
            norm = (sum(x*x for x in a) ** 0.5) * (sum(y*y for y in b) ** 0.5)
            cos = dot / norm
            status = "PASS" if cos >= 0.9999 else "FAIL"
            print(f"[embed-parity] {status} cosine={cos:.6f} text={text[:80]!r}", flush = True)

    
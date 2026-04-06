from typing import List, Dict, Any, Optional, Tuple
import re
import logging
from app.core.rag import RAGPipeline
from app.core.vector_store import VectorStore
from app.core.embeddings import EmbeddingModel
from app.services.document_processor import DocumentProcessor
from app.config import settings

logger = logging.getLogger(__name__)

class ChatService:
    """Main service for chatbot interactions."""
    
    def __init__(self):
        self.embedding_model = EmbeddingModel()
        self.vector_store = VectorStore()
        self.document_rag_pipeline = RAGPipeline(
            vector_store=self.vector_store,
            embedding_model=self.embedding_model,
        )

        video_index_name = getattr(settings, "PINECONE_VIDEO_INDEX_NAME", self.vector_store.index_name)
        if video_index_name == self.vector_store.index_name:
            self.video_vector_store = self.vector_store
        else:
            self.video_vector_store = VectorStore(index_name=video_index_name)
        self.video_rag_pipeline = RAGPipeline(
            vector_store=self.video_vector_store,
            embedding_model=self.embedding_model,
        )

        self._log_vector_store_details(self.vector_store, "Document")
        self._log_vector_store_details(self.video_vector_store, "Video")

        self.document_processor = DocumentProcessor()
    
    def search_documents(self, query: str, top_k: int = None) -> Dict[str, Any]:
        """Search for relevant document chunks."""
        try:
            if top_k is None:
                top_k = settings.DEFAULT_TOP_K
            
            # Check if vector store has any data
            if self._is_vector_store_empty():
                return {
                    "success": False,
                    "query": query,
                    "results": [],
                    "total_results": 0,
                    "error": "No documents have been ingested yet. Please use the /ingest endpoint to upload and process documents."
                }
            
            context_chunks = self.document_rag_pipeline.retrieve_context(query, top_k)
            
            if not context_chunks:
                return {
                    "success": True,
                    "query": query,
                    "results": [],
                    "total_results": 0,
                    "message": "No relevant documents found for your query. Try different keywords or check if documents covering this topic have been uploaded."
                }
            
            return {
                "success": True,
                "query": query,
                "results": context_chunks,
                "total_results": len(context_chunks)
            }
            
        except Exception as e:
            logger.error(f"Error searching documents: {e}")
            return {
                "success": False,
                "error": str(e),
                "query": query,
                "results": []
            }
    
    def ask_question(self, question: str, top_k: int = None, conversation_history: Optional[List[Dict[str, str]]] = None) -> Dict[str, Any]:
        """Get context-aware response to a question (documents + general knowledge).
        
        Args:
            question: User's question
            top_k: Number of context chunks to retrieve
            conversation_history: Optional list of previous messages in format [{"role": "user"|"assistant", "content": "text"}]
        """
        try:
            if top_k is None:
                top_k = settings.DEFAULT_TOP_K
            
            # Check if vector store has any data
            if self._is_vector_store_empty():
                return {
                    "success": False,
                    "question": question,
                    "answer": "No documents have been ingested yet. Please use the /ingest endpoint to upload and process documents before asking questions.",
                    "context_used": [],
                    "confidence": 0.0,
                    "error": "empty_vector_store"
                }
            
            # Retrieve relevant context
            context_chunks = self.document_rag_pipeline.retrieve_context(question, top_k)
            
            # Filter and rank images from context chunks
            relevant_images = self._filter_and_rank_images(context_chunks, max_images=3, min_score=0.3)
            
            # Format context (will be empty string if no chunks)
            formatted_context = self.document_rag_pipeline.format_context(context_chunks) if context_chunks else ""

            # If an LLM is configured, generate a grounded answer; otherwise use simple stub
            answer = ""
            image_positions = []
            if settings.AZURE_OPENAI_API_KEY and settings.AZURE_OPENAI_ENDPOINT:
                try:
                    answer, image_positions = self._generate_llm_answer(question, formatted_context, relevant_images, conversation_history)
                    # Remove image markers and chunk citations from answer text
                    import re
                    answer = re.sub(r'\[IMAGE:\s*[^\]]+\]', '', answer)
                    answer = re.sub(r'\[CHUNKS_CITED:[^\]]+\]', '', answer)
                    answer = answer.strip()
                except Exception as llm_exc:
                    logger.warning(f"LLM generation failed, falling back to stub: {llm_exc}")
                    # Fallback to simple answer (handles empty chunks)
                    answer = self._generate_simple_answer(question, context_chunks, formatted_context)
            else:
                answer = self._generate_simple_answer(question, context_chunks, formatted_context)
            
            # Build image references with positions
            image_references = []
            if relevant_images:
                # Create a map of path to image metadata
                image_map = {img['path']: img for img in relevant_images}
                
                # If we have positions from LLM, use them
                if image_positions:
                    for pos_info in image_positions:
                        path = pos_info['path']
                        if path in image_map:
                            img_meta = image_map[path]
                            image_references.append({
                                'path': path,
                                'position': pos_info['position'],
                                'alt_text': img_meta.get('section_title', 'Document image'),
                                'relevance_score': img_meta.get('score'),
                                'context_text': img_meta.get('context_text', ''),
                            })
                else:
                    # No positions from LLM - it determined images aren't relevant
                    # Don't show any images (respect the LLM's decision)
                    pass

            return {
                "success": True,
                "question": question,
                "answer": answer,
                "context_used": context_chunks,
                "confidence": self._calculate_confidence(context_chunks),
                "relevant_images": image_references,
                **self._extract_primary_video_reference([]),
            }
            
        except Exception as e:
            logger.error(f"Error answering question: {e}")
            return {
                "success": False,
                "error": str(e),
                "question": question,
                "answer": None
            }

    def ask_video_question(self, question: str, top_k: int = None) -> Dict[str, Any]:
        """Answer a question using only video transcript context."""
        try:
            if top_k is None:
                top_k = settings.DEFAULT_TOP_K

            if self._is_vector_store_empty(self.video_vector_store):
                return {
                    "success": False,
                    "question": question,
                    "answer": "No content has been ingested yet. Please upload and process video transcripts before asking questions.",
                    "context_used": [],
                    "confidence": 0.0,
                    "error": "empty_vector_store"
                }

            metadata_filter = {"source_type": {"$eq": "video"}}
            logger.info(
                "ask_video_question retrieval -> index=%s namespace=%s filter=%s",
                self.video_vector_store.index_name,
                getattr(self.video_vector_store, "namespace", None) or "default",
                metadata_filter,
            )

            context_chunks = self.video_rag_pipeline.retrieve_context(
                question,
                top_k,
                metadata_filter=metadata_filter,
            )
            if not context_chunks:
                logger.warning("No video matches found with metadata filter; retrying without filter for diagnostics.")
                context_chunks = self.video_rag_pipeline.retrieve_context(question, top_k)

            video_context = self._build_video_context(context_chunks)

            if not context_chunks:
                return {
                    "success": True,
                    "question": question,
                    "answer": "I couldn't find any video transcripts related to that question. Please try rephrasing or pick another video topic.",
                    "context_used": [],
                    "confidence": 0.0,
                    "video_context": [],
                    **self._extract_primary_video_reference([]),
                }

            answer = self._format_video_resource_answer(context_chunks)
            answer = self._attach_video_references(answer, video_context)

            return {
                "success": True,
                "question": question,
                "answer": answer,
                "context_used": context_chunks,
                "confidence": self._calculate_confidence(context_chunks),
                "video_context": video_context,
                **self._extract_primary_video_reference(video_context),
            }

        except Exception as e:
            logger.error(f"Error answering video question: {e}")
            return {
                "success": False,
                "error": str(e),
                "question": question,
                "answer": None
            }
    
    def _generate_simple_answer(self, question: str, context_chunks: List[Dict[str, Any]], formatted_context: str) -> str:
        """Generate a concise answer from context when no LLM is available."""
        if not context_chunks:
            return "I couldn't find relevant information in the uploaded content for that question."

        top_chunks = context_chunks[:3]
        summaries: List[str] = []
        for chunk in top_chunks:
            source = chunk.get("section_title") or chunk.get("source") or "Document"
            snippet = (chunk.get("text") or "").strip()
            if len(snippet) > 260:
                snippet = snippet[:257].rstrip() + "..."
            summaries.append(f"{source}: {snippet}")

        return "\n\n".join(summaries)

    def _format_video_resource_answer(self, context_chunks: List[Dict[str, Any]]) -> str:
        """List relevant video clips with timestamps and short descriptions."""
        entries: List[str] = []
        seen_keys = set()

        for chunk in context_chunks:
            video_url = chunk.get("video_url")
            if not video_url:
                continue

            key = (video_url, chunk.get("start_seconds"))
            if key in seen_keys:
                continue
            seen_keys.add(key)

            title = chunk.get("source") or "Video"
            start_label = self._format_timestamp(chunk.get("start_seconds")) or "00:00"
            end_label = self._format_timestamp(chunk.get("end_seconds"))
            time_label = f"{start_label} → {end_label}" if end_label and end_label != start_label else start_label
            description = self._summarize_clip_text(chunk.get("text") or "")

            entries.append(f"- {title} ({time_label}): {description}")
            if len(entries) == 4:
                break

        if not entries:
            return "I couldn't find any video snippets related to that question."

        return "Here are some video resources that might help you:\n" + "\n".join(entries)

    def _extract_summary_points(self, text: str) -> List[str]:
        """Split transcript text into paraphrased bullet-friendly statements."""
        sentences = re.split(r"(?<=[.!?])\s+", text.strip())
        points: List[str] = []
        for sentence in sentences:
            cleaned = self._paraphrase_sentence(sentence)
            if not cleaned:
                continue
            if len(cleaned.split()) < 5:
                continue
            points.append(cleaned)
        return points

    def _paraphrase_sentence(self, sentence: str) -> str:
        """Lightly paraphrase a transcript sentence so it reads like a summary."""
        if not sentence:
            return ""
        text = sentence.strip()
        if not text:
            return ""

        text = re.sub(r"^[\"“”']+", "", text)
        text = re.sub(r"^(\s*(so|and|but|then)[, ]+)+", "", text, flags=re.IGNORECASE)

        replacements = [
            (r"\bwe're\b", "the presenter is"),
            (r"\bwe are\b", "the presenter is"),
            (r"\bwe\b", "the team"),
            (r"\byou can\b", "users can"),
            (r"\byou\b", "users"),
            (r"\byour\b", "a user's"),
            (r"\bI'm\b", "The presenter is"),
            (r"\bI'll\b", "The presenter will"),
            (r"\bwe've\b", "the team has"),
            (r"\bwe'll\b", "the team will"),
            (r"\blet's\b", "the workflow"),
            (r"\bright\b", ""),
        ]
        for pattern, repl in replacements:
            text = re.sub(pattern, repl, text, flags=re.IGNORECASE)

        text = re.sub(
            r"^The presenter is going to (?:go ahead and )?",
            "Demonstrates how to ",
            text,
            flags=re.IGNORECASE,
        )
        text = re.sub(
            r"^The presenter is (?:going to )?",
            "Explains how to ",
            text,
            flags=re.IGNORECASE,
        )
        text = re.sub(
            r"^The presenter will",
            "Shows how to",
            text,
            flags=re.IGNORECASE,
        )
        text = re.sub(
            r"^Users can",
            "Highlights how users can",
            text,
            flags=re.IGNORECASE,
        )
        text = re.sub(
            r"^Users",
            "Highlights how users",
            text,
            flags=re.IGNORECASE,
        )

        text = re.sub(r"go ahead and\s+", "", text, flags=re.IGNORECASE)
        text = re.sub(r"\b(?:right|okay|um|uh)\b", "", text, flags=re.IGNORECASE)
        text = re.sub(r"\s+", " ", text).strip(" ,.-")
        if not text:
            return ""
        if not text[0].isupper():
            text = text[0].upper() + text[1:]
        if not text.endswith("."):
            text += "."
        return text

    def _summarize_clip_text(self, text: str) -> str:
        """Return a concise description for a transcript chunk."""
        points = self._extract_summary_points(text)
        if points:
            return self._normalize_description(points[0])

        snippet = (text or "").strip()
        snippet = re.sub(r"\s+", " ", snippet)
        snippet = re.sub(r"[\"“”']", "", snippet)
        snippet = snippet.rstrip(".!?")
        if len(snippet) > 160:
            snippet = snippet[:157].rstrip() + "..."
        if not snippet:
            return "Describes the relevant workflow in the clip."
        return f"Focuses on {snippet.lower()}."

    def _normalize_description(self, statement: str) -> str:
        """Convert a paraphrased sentence into a short descriptive clause."""
        desc = statement.strip()
        if not desc:
            return "Describes the relevant workflow in the clip."

        desc = desc.rstrip(".!?")
        desc = re.sub(
            r"^(Explains|Demonstrates|Shows|Highlights|Describes)\s+how\s+to\s+",
            "",
            desc,
            flags=re.IGNORECASE,
        )
        desc = re.sub(
            r"^(Explains|Demonstrates|Shows|Highlights|Describes)\s+",
            "",
            desc,
            flags=re.IGNORECASE,
        )
        desc = re.sub(
            r"^(Focuses on|Details|Covers)\s+",
            "",
            desc,
            flags=re.IGNORECASE,
        )
        desc = re.sub(r"\b(?:right|okay|um|uh)\b", "", desc, flags=re.IGNORECASE)
        desc = re.sub(r"\s+", " ", desc).strip(" ,.-")

        if not desc:
            return "Describes the relevant workflow in the clip."

        lower = desc[0].lower() + desc[1:] if len(desc) > 1 else desc.lower()
        return f"Focuses on {lower}."

    def _generate_llm_answer(self, question: str, formatted_context: str, available_images: List[Dict[str, Any]] = None, conversation_history: Optional[List[Dict[str, str]]] = None) -> Tuple[str, List[Dict[str, Any]]]:
        """Use OpenAI GPT-4o-mini to generate a grounded answer from context.
        
        Args:
            question: User's question
            formatted_context: Formatted context text with [CHUNK_ID: xxx] markers
            available_images: List of available images with metadata (including chunk_id)
            conversation_history: Optional list of previous messages in format [{"role": "user"|"assistant", "content": "text"}]
        
        Returns:
            Tuple of (answer_text, image_positions) where image_positions is a list of
            dicts with 'position' (int), 'path' (str), and 'chunk_id' (str) keys
        """
        system_prompt = (
            "You are a helpful assistant that answers questions with help from the provided context. "
            "If the context does not directly contain the answer, use the context to answer to the best of your ability. "
            "If the question is any kind of small talk, such as a greeting or thanking you, respond accordingly and kindly. "
            "If the question is very clearly unrelated to the context, say that you're unsure and offer to help with something else. "
            "Respond clearly and concisely.\n\n"
            "IMPORTANT: At the end of your response, list which chunks you actually cited to form your answer. "
            "Format them as: [CHUNKS_CITED: chunk_id_1, chunk_id_2, chunk_id_3, ...]. "
            "Only list chunks you directly referenced or drew information from. Even if only using one chunk, use this format. "
            "This helps us show only relevant images from the chunks you actually used."
        )
        
        # Build image context if available
        image_context = ""
        if available_images:
            image_list = []
            for img in available_images:
                path = img.get('path', '')
                chunk_id = img.get('chunk_id', 'unknown')
                context_text = img.get('context_text', '')[:150]  # Truncate for prompt
                image_list.append(f"- [IMAGE: {path}] (from [CHUNK_ID: {chunk_id}]) - Context: {context_text}")
            
            image_context = (
                "\n\nAvailable relevant images (include [IMAGE: path] markers in your response when appropriate):\n"
                + "\n".join(image_list) + 
                "\n\nWhen your answer would benefit from showing an image, include [IMAGE: path] at the "
                "point in your response where the image should appear. Place images immediately after the sentence "
                "that describes what the corresponding image illustrates. Only reference images from chunks you cited "
                "in [CHUNKS_CITED: ...]. Do not place an image marker between a sentence and "
                "its corresponding punctuation. Do not place an image marker somewhere that will break up a sentence."
            )
        
        user_prompt = (
            f"Question:\n{question}\n\n"
            f"Context (extracts from company docs):\n{formatted_context}{image_context}\n\n"
            "Answer:"
        )

        if settings.AZURE_OPENAI_API_KEY and settings.AZURE_OPENAI_ENDPOINT:
            try:
                from openai import AzureOpenAI  # type: ignore
            except Exception as exc:
                raise RuntimeError(f"OpenAI SDK not available: {exc}")

            client = AzureOpenAI(
                api_key=settings.AZURE_OPENAI_API_KEY,
                azure_endpoint=settings.AZURE_OPENAI_ENDPOINT,
                api_version=settings.AZURE_OPENAI_API_VERSION,
                timeout=30.0,    # seconds — prevents indefinite hang on slow deployments
                max_retries=2,   # retry transient network errors before raising
            )
            model_name = settings.AZURE_OPENAI_DEPLOYMENT

            # Build messages array with conversation history
            messages = [{"role": "system", "content": system_prompt}]

            # Add conversation history if provided
            if conversation_history:
                for msg in conversation_history:
                    role = msg.get("role", "user")
                    content = msg.get("content", "")
                    if role in ["user", "assistant"] and content:
                        messages.append({"role": role, "content": content})

            # Add current question
            messages.append({"role": "user", "content": user_prompt})

            resp = client.chat.completions.create(
                model=model_name,
                messages=messages,
                temperature=0.2,
                max_tokens=500,
            )
            content = resp.choices[0].message.content if resp and resp.choices else None
            if not content:
                raise RuntimeError("Empty response from Azure OpenAI")
            answer_text = content.strip()
            cited_chunks = self._extract_cited_chunks(answer_text)
            image_positions = self._parse_image_references_by_chunks(answer_text, available_images or [], cited_chunks)
            return answer_text, image_positions

        raise RuntimeError("Azure OpenAI API key and endpoint must both be configured")


    
    def _extract_cited_chunks(self, answer_text: str) -> set:
        """Extract chunk IDs from [CHUNKS_CITED: ...] marker in LLM response.
        
        Args:
            answer_text: LLM response text
        
        Returns:
            Set of chunk IDs that the LLM cited
        """
        import re
        
        # Look for [CHUNKS_CITED: chunk1, chunk2, ...] pattern
        match = re.search(r'\[CHUNKS_CITED:\s*([^\]]+)\]', answer_text)
        if not match:
            return set()
        
        cited_text = match.group(1)
        # Split by comma and strip whitespace
        chunk_ids = {cid.strip() for cid in cited_text.split(',') if cid.strip()}
        return chunk_ids
    
    def _parse_image_references_by_chunks(self, answer_text: str, available_images: List[Dict[str, Any]], cited_chunks: set) -> List[Dict[str, Any]]:
        """Parse [IMAGE: path] markers from LLM response and filter by cited chunks.
        
        Args:
            answer_text: LLM response text that may contain [IMAGE: path] markers
            available_images: List of available images to validate against
            cited_chunks: Set of chunk IDs that the LLM cited
        
        Returns:
            List of dicts with 'position' (character index), 'path' (image path), and 'chunk_id'
        """
        import re
        
        # If no chunks were cited explicitly, include all images (fallback)
        # Otherwise, only include images from cited chunks
        valid_images = available_images
        if cited_chunks:
            valid_images = [img for img in available_images if img.get('chunk_id') in cited_chunks]
        
        # Create a set of valid image paths for quick lookup
        valid_paths = {img.get('path', ''): img for img in valid_images}
        
        # Find all [IMAGE: path] markers
        pattern = r'\[IMAGE:\s*([^\]]+)\]'
        matches = list(re.finditer(pattern, answer_text, re.IGNORECASE))
        
        image_positions = []
        for match in matches:
            path = match.group(1).strip()
            # Only include if path is in available images
            if path in valid_paths:
                position = match.start()  # Position where marker starts
                img_meta = valid_paths[path]
                image_positions.append({
                    'position': position,
                    'path': path,
                    'chunk_id': img_meta.get('chunk_id', ''),
                })
        
        # Sort by position
        image_positions.sort(key=lambda x: x['position'])
        
        return image_positions
    
    def _filter_and_rank_images(self, context_chunks: List[Dict[str, Any]], max_images: int = 3, min_score: float = 0.3) -> List[Dict[str, Any]]:
        """Filter and rank images from context chunks by relevance.
        
        Args:
            context_chunks: List of context chunks with image_paths
            max_images: Maximum number of images to return (default 3)
            min_score: Minimum relevance score threshold (default 0.3)
        
        Returns:
            List of image metadata dictionaries with path, score, rank, context_text, etc.
        """
        image_candidates = []
        
        for chunk in context_chunks:
            score = chunk.get('score', 0.0)
            # Skip low-relevance chunks
            if score < min_score:
                continue
            
            image_paths = chunk.get('image_paths', [])
            if not image_paths:
                continue
            
            # Extract context text (first 200 chars for brevity)
            context_text = (chunk.get('text', '') or '')[:200]
            section_title = chunk.get('section_title', '')
            
            for img_path in image_paths:
                if not img_path or not isinstance(img_path, str):
                    continue
                
                image_candidates.append({
                    'path': img_path,
                    'score': score,
                    'rank': chunk.get('rank', 999),
                    'context_text': context_text,
                    'section_title': section_title,
                    'chunk_id': chunk.get('chunk_id', ''),
                })
        
        if not image_candidates:
            return []
        
        # Sort by score descending, then by rank ascending (lower rank = better)
        image_candidates.sort(key=lambda x: (-x['score'], x['rank']))
        
        # Deduplicate by path, keeping the one with highest score
        seen = {}
        for img in image_candidates:
            path = img['path']
            if path not in seen or seen[path]['score'] < img['score']:
                seen[path] = img
        
        # Return top N images
        return list(seen.values())[:max_images]
    
    def _calculate_confidence(self, context_chunks: List[Dict[str, Any]]) -> float:
        """Calculate confidence score based on context quality."""
        if not context_chunks:
            return 0.0
        
        # Simple confidence calculation based on top match score
        top_score = context_chunks[0].get('score', 0.0)
        
        # Normalize score to 0-1 range (assuming Pinecone cosine similarity)
        confidence = max(0.0, min(1.0, top_score))
        
        return round(confidence, 3)

    def _build_video_context(self, context_chunks: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Collect per-video metadata that callers can render."""
        clips: List[Dict[str, Any]] = []
        for chunk in context_chunks:
            if chunk.get("source_type") != "video":
                continue
            video_url = chunk.get("video_url")
            if not video_url:
                continue

            start = chunk.get("start_seconds")
            end = chunk.get("end_seconds")
            clip = {
                "video_url": video_url,
                "start_seconds": start,
                "end_seconds": end,
                "timestamp": self._format_timestamp(start),
                "end_timestamp": self._format_timestamp(end),
                "deep_link_url": self._build_video_link(video_url, start),
                "preview": (chunk.get("text") or "")[:240],
            }
            clips.append(clip)
        return clips

    def _format_timestamp(self, seconds: Optional[float]) -> Optional[str]:
        """Return HH:MM:SS timestamp for display."""
        if seconds is None:
            return None
        try:
            seconds = max(0, int(float(seconds)))
        except (TypeError, ValueError):
            return None
        h, rem = divmod(seconds, 3600)
        m, s = divmod(rem, 60)
        if h:
            return f"{h:02d}:{m:02d}:{s:02d}"
        return f"{m:02d}:{s:02d}"

    def _build_video_link(self, video_url: str, start_seconds: Optional[float]) -> str:
        """Append a fragment so web players can seek directly."""
        if start_seconds is None:
            return video_url
        try:
            start = max(0, int(float(start_seconds)))
        except (TypeError, ValueError):
            return video_url
        return f"{video_url}#t={start}"

    def _attach_video_references(self, answer: str, video_context: List[Dict[str, Any]]) -> str:
        """Append human-readable video references to the answer text."""
        if not video_context:
            return answer

        lines = ["Video reference:" if len(video_context) == 1 else "Video references:"]
        for clip in video_context[:3]:
            timestamp = clip.get("timestamp") or f"{int(clip.get('start_seconds', 0))}s"
            link = clip.get("deep_link_url") or clip.get("video_url")
            if not link:
                continue
            lines.append(f"- {timestamp}: {link}")

        if len(lines) == 1:
            return answer
        joiner = "\n".join(lines)
        if answer.endswith("\n"):
            return f"{answer.rstrip()}\n\n{joiner}"
        return f"{answer}\n\n{joiner}"
    
    def _extract_primary_video_reference(self, video_context: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Return a single clip descriptor for clients that only need one video/timestamp."""
        default = {
            "answer_video_url": None,
            "answer_start_seconds": None,
            "answer_end_seconds": None,
            "answer_timestamp": None,
            "answer_end_timestamp": None,
        }
        if not video_context:
            return default
        clip = video_context[0]
        return {
            "answer_video_url": clip.get("deep_link_url") or clip.get("video_url"),
            "answer_start_seconds": clip.get("start_seconds"),
            "answer_end_seconds": clip.get("end_seconds"),
            "answer_timestamp": clip.get("timestamp"),
            "answer_end_timestamp": clip.get("end_timestamp"),
        }

    def _log_vector_store_details(self, store: VectorStore, label: str) -> None:
        """Log which Pinecone index/namespace a store is targeting."""
        namespace = getattr(store, "namespace", None) or "default"
        logger.info("%s vector store configured -> index=%s namespace=%s", label, store.index_name, namespace)
    
    def get_recommendations(self, query: str, content_type: str = "all") -> Dict[str, Any]:
        """Get content recommendations based on query."""
        try:
            # Search for relevant content
            search_result = self.search_documents(query, top_k=10)
            
            if not search_result["success"]:
                return search_result
            
            # Group by source and content type
            recommendations = {
                "documents": [],
                "videos": [],
                "related_topics": []
            }
            
            for result in search_result["results"]:
                source_type = result.get("source_type", "document")
                
                rec_item = {
                    "title": result.get("source"),
                    "relevance_score": result.get("score"),
                    "preview": (result.get("text") or "")[:200] + "...",
                    "source_type": source_type,
                    "doc_id": result.get("doc_id"),
                    "section_id": result.get("section_id"),
                    "section_path": result.get("section_path"),
                    "section_title": result.get("section_title"),
                    "image_paths": result.get("image_paths", []),
                }
                
                if source_type == "video":
                    recommendations["videos"].append(rec_item)
                else:
                    recommendations["documents"].append(rec_item)
            
            # Remove duplicates and sort by relevance
            recommendations["documents"] = self._deduplicate_recommendations(recommendations["documents"])
            recommendations["videos"] = self._deduplicate_recommendations(recommendations["videos"])
            
            return {
                "success": True,
                "query": query,
                "recommendations": recommendations,
                "total_items": len(recommendations["documents"]) + len(recommendations["videos"])
            }
            
        except Exception as e:
            logger.error(f"Error getting recommendations: {e}")
            return {
                "success": False,
                "error": str(e),
                "query": query,
                "recommendations": {"documents": [], "videos": [], "related_topics": []}
            }
    
    def _is_vector_store_empty(self, store: Optional[VectorStore] = None) -> bool:
        """Return True when the vector store has no stored vectors."""
        try:
            target_store = store or self.vector_store
            stats = target_store.get_index_stats()
        except AttributeError:
            logger.warning("VectorStore is missing get_index_stats(); skipping empty-store check.")
            return False
        except Exception as e:
            logger.warning(f"Could not check vector store stats: {e}")
            return False

        total_vectors = stats.get("total_vector_count")
        if total_vectors is None:
            namespaces = stats.get("namespaces") or {}
            total_vectors = sum(ns.get("vectorCount", 0) for ns in namespaces.values())

        return total_vectors == 0

    def _deduplicate_recommendations(self, items: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Remove duplicate recommendations based on title."""
        seen_titles = set()
        unique_items = []
        
        for item in sorted(items, key=lambda x: x["relevance_score"], reverse=True):
            if item["title"] not in seen_titles:
                seen_titles.add(item["title"])
                unique_items.append(item)
        
        return unique_items[:5]  # Limit to top 5





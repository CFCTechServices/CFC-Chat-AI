from pydantic import BaseModel, Field
from typing import Optional, List

class IngestRequest(BaseModel):
    """Request model for document ingestion."""
    filename: str = Field(..., description="Name of the file to ingest")
    
class BulkIngestRequest(BaseModel):
    """Request model for bulk document ingestion."""
    subdirectory: Optional[str] = Field(None, description="Optional subdirectory to process")

class SearchRequest(BaseModel):
    """Request model for document search."""
    query: str = Field(..., description="Search query")
    top_k: Optional[int] = Field(5, description="Number of results to return", ge=1, le=20)

class Message(BaseModel):
    """Message model for conversation history."""
    role: str = Field(..., description="Message role: 'user' or 'assistant'")
    content: str = Field(..., description="Message content")

class AskRequest(BaseModel):
    """Request model for asking questions."""
    question: str = Field(..., description="Question to ask")
    top_k: Optional[int] = Field(4, description="Number of context chunks to use", ge=1, le=10)
    conversation_history: Optional[List[Message]] = Field(None, description="Previous messages in the conversation")

class RecommendationRequest(BaseModel):
    """Request model for getting recommendations."""
    query: str = Field(..., description="Query for recommendations")
    content_type: Optional[str] = Field("all", description="Type of content to recommend (all, documents, videos)")
    limit: Optional[int] = Field(5, description="Maximum number of recommendations", ge=1, le=20)
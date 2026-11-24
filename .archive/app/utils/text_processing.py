from typing import List
import re
from app.config import settings

def split_into_chunks(text: str, chunk_size: int = None, overlap: int = None) -> List[str]:
    """
    Split text into overlapping chunks.
    
    Args:
        text: Input text to chunk
        chunk_size: Maximum size of each chunk
        overlap: Number of characters to overlap between chunks
        
    Returns:
        List of text chunks
    """
    if chunk_size is None:
        chunk_size = settings.CHUNK_SIZE
    if overlap is None:
        overlap = settings.CHUNK_OVERLAP
    
    # Clean up text
    text = re.sub(r"\n{3,}", "\n\n", text).strip()
    
    if len(text) <= chunk_size:
        return [text] if text else []
    
    chunks = []
    start = 0
    
    while start < len(text):
        end = min(len(text), start + chunk_size)
        
        # Try to find a good breaking point (sentence end)
        if end < len(text):
            # Look for sentence breaks within the last 50% of the chunk
            break_point = text.rfind(".", start + chunk_size // 2, end)
            if break_point == -1:
                # Look for other punctuation
                for punct in ["!", "?", "\n\n", "\n"]:
                    break_point = text.rfind(punct, start + chunk_size // 2, end)
                    if break_point != -1:
                        break
            
            if break_point != -1 and break_point > start:
                end = break_point + 1
        
        chunk = text[start:end].strip()
        if chunk:
            chunks.append(chunk)
        
        # Move start position considering overlap
        start = max(end - overlap, end)
        
        # Avoid infinite loops
        if start >= len(text):
            break
    
    return chunks

def clean_text(text: str) -> str:
    """Clean and normalize text."""
    # Remove excessive whitespace
    text = re.sub(r'\s+', ' ', text)
    
    # Remove special characters that might cause issues
    text = re.sub(r'[^\w\s\.,!?;:()\[\]"\'-]', '', text)
    
    # Normalize quotes
    text = re.sub(r'["""]', '"', text)
    text = re.sub(r"[''']", "'", text)
    
    return text.strip()

def extract_metadata_from_text(text: str) -> dict:
    """Extract basic metadata from text content."""
    metadata = {
        'word_count': len(text.split()),
        'char_count': len(text),
        'has_tables': 'table' in text.lower() or '|' in text,
        'has_code': 'def ' in text or 'function' in text or '```' in text,
        'has_urls': 'http' in text.lower() or 'www.' in text.lower()
    }
    
    return metadata
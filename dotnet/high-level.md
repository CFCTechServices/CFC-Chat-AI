# Pinecone, Supabase, Both, or None?
Does an endpoint use Pinecone, Supabase both, or none? This is useful to ask.

Results in dotnet\docs\pinecone-supabase-both-or-none.md

The endpoints that need extra consideration are the BOTH endpoints.

If and endpoint is non-used, don't worry about it for now.

## Both
Independent - GET /api/health/detailed
Not-used - POST /api/chat/search - Probably feedback. Yeah this is just the query but is also accounts for feedback. Also not even used by the current frontend, only the old one
Not-used - POST /api/chat/ask - Provides the chatbot answer, the context it used, relevant images. Everything tied together.
Non-used - POST /api/chat/ask/video - This just isn't used by the front-end. And through questioning I'm not even sure if it works. Probably skip for now.
Non-used - POST /api/chat/recommendations - Never called anywhere.
ChatService - POST /api/chat/message - This is the big one that is currently used. So much noise.
DocumentHandler - POST /api/files/upload - Used by the frontend
Non-used - POST /api/files/bulk - Unused and not necessary. A single endpoint can one or more files
DocumentHandler - POST /api/ingest/document - Not used, but might be nice to manually call this though.
Non-used POST /api/ingest/bulk - No need for an extra endpoint
DocumentHandler - DELETE /api/admin/documents/{doc_id} - Renamed to handler to support both adding and deleting
DocumentHandler - PUT /api/admin/documents/{doc_id}/replace
DocumentHandler - POST /api/videos/upload - Then have a separate VideoProcessor

DocumentProcessor isn't listed but likely deserves it's own folder and interface. Just turns various file types into text chunks

VideoProcessor should do the exact same thing as DocumentProcessor but for videos


# High-level component connections
Both Pinecone and Supabase are completely isolated and do not touch anything else besides their respective service.

## Feedback
Should only use Pinecone and Supabase. Input a query, return reranked chunks.

## DocumentProcessor
Independent. Should input a supported document and extract text chunks from it.

## VideoProcessor
Independent. Should input a video and extract text chunks from it using transcription.

## DocumentHandler
Uses both DocumentProcessor and VideoProcessor to acquire text chunks from documents. Then ingests them into Supabase and Pinecone. Renamed to handler because it should also support deleting alongside ingestion.

## ChatService
Uses everything besides direct calls to DocumentProcessor and VideoProcessor. Ties everything together to create the ChatBot. Basically the 'POST /api/chat/message' endpoint.

## Api
Uses everything including ChatService, excluding direct calls to Document/VideoProcessor. Defines the internal endpoints.
Agents don't touch.

# Pinecone, Supabase, Both, or None?
Does an endpoint use Pinecone, Supabase both, or none? This is useful to ask.

Results in dotnet\docs\pinecone-supabase-both-or-none.md

The endpoints that need extra consideration are the BOTH endpoints.

If an endpoint is non-used, don't worry about it for now.

## Both
Independent - GET /api/health/detailed
Non-used - POST /api/chat/search - Probably feedback. Yeah this is just the query but is also accounts for feedback. Also not even used by the current frontend, only the old one
Non-used - POST /api/chat/ask - Provides the chatbot answer, the context it used, relevant images. Everything tied together.
Non-used - POST /api/chat/ask/video - This just isn't used by the front-end. And through questioning I'm not even sure if it works. Probably skip for now.
Non-used - POST /api/chat/recommendations - Never called anywhere.
ChatService - POST /api/chat/message - This is the big one that is actually in use. So much noise.
DocumentHandler - POST /api/files/upload - Used by the frontend
Non-used - POST /api/files/bulk - Unused and not necessary. A single endpoint can input one or more files
DocumentHandler - POST /api/ingest/document - Not used, but might be nice to manually call this though.
Non-used POST /api/ingest/bulk - No need for an extra endpoint
DocumentHandler - DELETE /api/admin/documents/{doc_id} - Renamed to handler to support both adding and deleting
DocumentHandler - PUT /api/admin/documents/{doc_id}/replace - Just calls the DocumentHandler delete and then ingest
DocumentHandler - POST /api/videos/upload - Then have a separate VideoProcessor

DocumentProcessor isn't listed but likely deserves it's own folder and interface. Just turns various file types into text chunks

VideoProcessor should do the exact same thing as DocumentProcessor but for videos


# High-level component connections
Both Pinecone and Supabase are completely isolated and do not touch anything else besides their respective service.

## Feedback
Should only use Pinecone and Supabase. Input a query, return reranked chunks.

## DocumentProcessor
Independent. Inputs a supported document type and outputs text chunks. Should not touch anything else.

## VideoProcessor
Independent. Should input a video and extract text chunks from it using transcription.

## DocumentHandler
Uses both DocumentProcessor and VideoProcessor to acquire text chunks from documents. Then ingests them into Supabase and Pinecone. Renamed to handler because it should also support deleting alongside ingestion.

## ChatService
Uses everything besides direct calls to DocumentProcessor and VideoProcessor. Ties everything together to create the ChatBot. Basically the 'POST /api/chat/message' endpoint.

## Api
Uses everything including ChatService, excluding direct calls to Document/VideoProcessor. Defines the internal endpoints.

# Folder Structure
Three rules should be applied to each component:
1. Non-interface prefix
2. One data-structure per file
3. Only 3 files in component root

## Non-interface prefix
Any implementation files/folders, or anything not important to a user wanting to use a given layer, should be prefixed with '_'. If someone wants to make a call to Supabase, it should be very obvious where to look. They shouldn't have to guess, or block out irrelevant information for their use. The result of this rule:
1. All implementations of the interface are under _Internal/
2. Properties/ is renamed to _Properties/

## One data-structure per file
Any public data-structure should have one file named exactly the same as it's class/record. Why? Because that means from the file-tree I can immediately see where every data structure is. No sifting through a large 200 line file of 5 data structures to find the definition of the one you need.
This applies for interfaces too, the great thing is that every interface starts with a 'I' (ex: IPineconeService.cs), that means that you can immediately tell the difference between a data structure and an interface directly from the file-tree. On top of knowing there is no hidden structures within those files.

## Only 3 files in component root
For the purposes of keeping things organized, only 3 files are allowed in the folder root. Everything else should be organized in folders.

The 3 files are:
1. DepencyInjection.cs
2. <ComponentName>Options.cs
2. CFC.ChatAI.<ComponentName>.csproj

Let's not sprawl out 10 files in the root. DependencyInjection and the Options file are the two main files that tie everything together into the actual app, that's why it makes sense for them to be kept outside of a folder.

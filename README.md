# CFC Chat-Talk - AI-Powered Documentation Chatbot

A Retrieval-Augmented Generation (RAG) chatbot system for CFC Technologies' animal feed software documentation. This FastAPI-based backend enables intelligent document search, Q&A capabilities, and video transcript processing through semantic search powered by Pinecone vector database.

## 📋 Table of Contents

- [Project Overview](#project-overview)
- [Key Components & Focus Areas](#key-components--focus-areas)
- [Important Files to Review](#important-files-to-review)
- [Credentials & Environment Setup](#credentials--environment-setup)
- [Quick Start Guide](#quick-start-guide)
- [Architecture Overview](#architecture-overview)
- [API Endpoints](#api-endpoints)
- [Next Steps & Improvements](#next-steps--improvements)
- [Support & Contact](#support--contact)
- [Project Status](#project-status)

---

<a id="project-overview"></a>
## 🎯 Project Overview

**CFC Chat-Talk** is a production-ready MVP that transforms company documentation (DOC/DOCX/TXT) and video transcripts into a searchable knowledge base. Users can ask natural language questions and receive AI-powered answers with source citations.

### Current Capabilities

✅ **Document Processing**: Upload and ingest DOC/DOCX/TXT files with automatic chunking and image extraction  
✅ **Semantic Search**: Vector-based search using Pinecone for finding relevant content  
✅ **AI Q&A**: Generate answers using OpenAI/Gemini with context from retrieved documents  
✅ **Video Processing**: Upload videos with Whisper transcription and searchable transcripts  
✅ **Web UI**: React-based frontend for document uploads and chat interactions  
✅ **Content Storage**: Local file storage with optional Supabase cloud integration  

### Technology Stack

- **Backend**: FastAPI (Python)
- **Vector Database**: Pinecone
- **Embeddings**: sentence-transformers (`all-MiniLM-L6-v2`)
- **LLM**: OpenAI GPT / Google Gemini (for answer generation)
- **Frontend**: React 18 (served as static files)
- **Storage**: Local filesystem + optional Supabase
- **Video Transcription**: OpenAI Whisper

---

<a id="key-components--focus-areas"></a>
## 🔧 Key Components & Focus Areas

The next team should prioritize these areas for production readiness:

### 1. **Authentication & Security** 🔐 (HIGH PRIORITY)
**Status**: Not implemented - Frontend-only role detection  
**Focus Files**:
- `app/auth/` - **Needs to be created** - JWT authentication, RBAC
- `main.py` (lines 47-53) - CORS configuration (currently allows all origins)
- `app/rate_limit/` - Rate limiting middleware

**What to Build**:
- JWT-based authentication system
- Role-based access control (Admin, Developer, User)
- API endpoint protection
- Secure session management
- Rate limiting per user/IP

### 2. **Chat History & Persistence** 💬 (HIGH PRIORITY)
**Status**: Messages only stored in React state (lost on refresh)  
**Focus Files**:
- `app/services/chat_history_service.py` - **Needs to be created**
- `app/api/endpoints/sessions.py` - **Needs to be created**
- `web/app.jsx` (lines 1321-1515) - ChatPage component

**What to Build**:
- Database schema for chat sessions and messages
- Backend API for session management
- Persistent chat history with user-specific retrieval
- Frontend integration for loading/saving conversations

### 3. **Feedback & Rating System** ⭐ (MEDIUM PRIORITY)
**Status**: Not implemented  
**Focus Files**:
- `app/api/endpoints/feedback.py` - **Needs to be created**
- `app/services/feedback_service.py` - **Needs to be created**
- `web/app.jsx` (lines 1097-1210) - ChatMessage component

**What to Build**:
- Thumbs up/down rating system
- Comment collection for feedback
- Analytics endpoint for admin dashboard
- Export functionality for ML training data

### 4. **RAG Quality Improvements** 🧠 (MEDIUM PRIORITY)
**Status**: Basic implementation - needs enhancement  
**Focus Files**:
- `app/core/rag.py` - Core RAG pipeline (lines 21-100)
- `app/services/chat_service.py` - Business logic (lines 81-165)
- `app/core/vector_store.py` - Vector search implementation
- `app/utils/text_processing.py` - Chunking strategies

**What to Improve**:
- Hybrid search (vector + keyword/BM25)
- Re-ranking with cross-encoder models
- Semantic chunking (sentence-aware, paragraph boundaries)
- Source citation with page/section numbers
- Query expansion and reformulation

### 5. **Testing & Observability** 📊 (MEDIUM PRIORITY)
**Status**: Basic unit tests only  
**Focus Files**:
- `tests/` - Expand test coverage
- `app/core/logging.py` - **Needs to be created** - Structured logging
- `app/core/metrics.py` - **Needs to be created** - Prometheus metrics
- `app/api/endpoints/health.py` - Enhance health checks

**What to Build**:
- Comprehensive test suite (unit + integration + E2E)
- Structured logging (JSON format)
- Application metrics (Prometheus)
- Error tracking (Sentry)
- Performance monitoring

### 6. **CFC System Integration** 🔗 (LOW PRIORITY)
**Status**: Not implemented  
**Focus Files**:
- `app/services/cfc_analytics_service.py` - **Needs to be created**

**What to Build**:
- Integration with CFC analytics platform
- Webhook/API calls for chat interactions
- Batch export of feedback data
- Usage metrics dashboard

---

<a id="important-files-to-review"></a>
## 📁 Important Files to Review

### Core Application Files

| File | Purpose | Key Lines |
|------|---------|-----------|
| `main.py` | Application entry point, router registration | 1-98 |
| `app/config.py` | Centralized configuration (API keys, paths, settings) | 1-59 |
| `requirements.txt` | Python dependencies | All |

### API Endpoints

| File | Purpose | Key Endpoints |
|------|---------|---------------|
| `app/api/endpoints/chat.py` | Search and Q&A endpoints | `/search`, `/ask`, `/recommendations` |
| `app/api/endpoints/upload.py` | File upload endpoints | `/api/files/upload` |
| `app/api/endpoints/ingest.py` | Document ingestion | `/api/ingest/document`, `/api/ingest/bulk` |
| `app/api/endpoints/videos.py` | Video processing | `/api/videos/upload` |
| `app/api/endpoints/health.py` | Health checks | `/health` |
| `app/api/endpoints/visibility.py` | System visibility | `/api/visibility/vector-store` |

### Core Services

| File | Purpose | Key Functionality |
|------|---------|-------------------|
| `app/services/chat_service.py` | Main chat business logic | `search_documents()`, `ask_question()` |
| `app/services/document_processor.py` | Document parsing and chunking | `process_document()` |
| `app/core/rag.py` | RAG pipeline implementation | `retrieve_context()`, `format_context()` |
| `app/core/vector_store.py` | Pinecone integration | `query()`, `upsert_vectors()` |
| `app/core/embeddings.py` | Embedding model wrapper | `encode_query()`, `encode_documents()` |

### Content Storage

| File | Purpose | Notes |
|------|---------|-------|
| `app/services/content_repository.py` | Local file storage | Default implementation |
| `app/services/supabase_content_repository.py` | Supabase cloud storage | Optional, enabled via config |

### Frontend

| File | Purpose | Key Components |
|------|---------|----------------|
| `web/app.jsx` | Main React application | Login, Chat, Admin, Upload flows |
| `web/styles.css` | Styling | All UI styles |
| `web/index.html` | HTML entry point | React app container |

### Configuration & Models

| File | Purpose |
|------|---------|
| `app/api/models/requests.py` | Request models (SearchRequest, AskRequest, etc.) |
| `app/api/models/responses.py` | Response models (SearchResponse, AskResponse, etc.) |

### Documentation

| File | Purpose |
|------|---------|
| `HANDOVER_DOCUMENT.md` | Comprehensive technical handover document |
| `SETUP_GUIDE.md` | Quick setup instructions |

---

<a id="credentials--environment-setup"></a>
## 🔑 Credentials & Environment Setup

### Required Environment Variables

Create a `.env` file in the project root with the following variables:

#### **Required Credentials** (Get from Dan Bates)

```bash
# Pinecone (REQUIRED for vector search)
# Contact Dan Bates from CFC Tech to obtain these credentials
PINECONE_API_KEY=your_pinecone_api_key_here
PINECONE_INDEX_NAME=cfc-animal-feed-chatbot
PINECONE_CLOUD=aws
PINECONE_REGION=us-east-1

# Optional: Separate index for videos
PINECONE_VIDEO_INDEX_NAME=cfc-animal-feed-chatbot-videos
PINECONE_NAMESPACE=your_namespace  # Optional
```

#### **Optional Credentials**

```bash
# Supabase (for cloud storage - optional)
# Contact Dan Bates from CFC Tech to obtain these credentials
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=your_supabase_anon_key
SUPABASE_BUCKET=your_bucket_name
SUPABASE_BUCKET_VIDEOS=your_videos_bucket_name

# OpenAI (for AI-generated answers)
# You can create your own account or ask Dan Bates
OPENAI_API_KEY=your_openai_api_key_here

# Google Gemini (alternative to OpenAI)
# You can create your own account
GEMINI_API_KEY=your_gemini_api_key_here
GEMINI_MODEL=gemini-2.0-flash
```

### How to Get Credentials

1. **Pinecone API Key** (REQUIRED): 
   - **Contact Dan Bates from CFC Tech** to get access to the Pinecone API key and index details
   - The existing Pinecone index is already configured with dimension `384` (matches `all-MiniLM-L6-v2` embedding model)
   - You'll receive: `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`, and other Pinecone configuration values

2. **Supabase Credentials** (Optional):
   - **Contact Dan Bates from CFC Tech** to get access to Supabase credentials
   - You'll receive: `SUPABASE_URL`, `SUPABASE_ANON_KEY`, and bucket names
   - These are optional - the system works with local file storage if Supabase is not configured

3. **Gemini API Key** (Optional - Alternative to OpenAI):
   - Get from [Google AI Studio](https://makersuite.google.com/app/apikey)
   - Can be used instead of OpenAI for answer generation

### Environment File Setup

```bash
# Windows (PowerShell)
Copy-Item -LiteralPath '.env.example' -Destination '.env'

# Windows (CMD)
copy .env.example .env

# macOS/Linux
cp .env.example .env
```

Then edit `.env` and add your credentials.

---

<a id="quick-start-guide"></a>
## 🚀 Quick Start Guide

### Prerequisites

- Python 3.8+ installed
- Virtual environment support
- Office/LibreOffice installed (for `.doc` file conversion on Windows)

### Installation Steps

1. **Clone the repository** (if not already done)
   ```bash
   git clone <repository-url>
   cd Chat-Talk
   ```

2. **Create and activate virtual environment**
   ```bash
   # Windows
   python -m venv .venv
   .venv\Scripts\activate

   # macOS/Linux
   python3 -m venv .venv
   source .venv/bin/activate
   ```

3. **Install dependencies**
   ```bash
   pip install -r requirements.txt
   ```

4. **Configure environment variables**
   ```bash
   # Copy example file
   copy .env.example .env  # Windows
   # or
   cp .env.example .env    # macOS/Linux

   # Edit .env and add your credentials (see above)
   ```

5. **Start the API server**
   ```bash
   uvicorn main:app --reload
   ```

6. **Access the application**
   - **Web UI**: [http://localhost:8000](http://localhost:8000)
   - **API Documentation**: [http://localhost:8000/docs](http://localhost:8000/docs)
   - **Health Check**: [http://localhost:8000/api/health](http://localhost:8000/api/health)

### First Steps After Setup

1. **Upload a document**:
   - Use the admin panel in the web UI at `http://localhost:8000` (drag & drop)
   - Or use the API: `POST /api/files/upload`

2. **Verify ingestion**:
   - Check `data/processed/content_repository/<doc-slug>/` for processed files
   - Check Pinecone index stats: `GET /api/visibility/vector-store`

3. **Test search**:
   - Use `/api/chat/search` endpoint or the web UI
   - Try `/api/chat/ask` for AI-generated answers

---

<a id="architecture-overview"></a>
## 🏗️ Architecture Overview

### System Flow

```
User Query → FastAPI → ChatService → RAGPipeline → VectorStore (Pinecone)
                                                      ↓
                                              Retrieved Chunks
                                                      ↓
                                              LLM (OpenAI/Gemini)
                                                      ↓
                                              Formatted Answer
```

### Component Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Frontend (React)                        │
│  web/app.jsx - Login, Chat, Admin, Upload interfaces        │
└───────────────────────┬─────────────────────────────────────┘
                        │ HTTP/REST
┌───────────────────────▼─────────────────────────────────────┐
│                   FastAPI Backend                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │ API Endpoints│  │   Services   │  │    Core      │     │
│  │ - chat.py    │→ │ - chat_svc   │→ │ - rag.py     │     │
│  │ - upload.py  │  │ - doc_proc   │  │ - embeddings│     │
│  │ - ingest.py  │  │ - content_repo│ │ - vector_db  │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└───────────────────────┬─────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
┌───────▼────┐  ┌───────▼────┐  ┌───────▼────┐
│  Pinecone  │  │  Local FS  │  │  Supabase  │
│ Vector DB  │  │  Storage   │  │  (Optional)│
└────────────┘  └────────────┘  └────────────┘
```

### Data Flow: Document Ingestion

```
Document Upload → DocumentProcessor → Chunking → Embeddings
                                              ↓
                                    VectorStore.upsert()
                                              ↓
                                    Pinecone Index
                                              ↓
                                    ContentRepository.save()
                                              ↓
                                    Local FS / Supabase
```

### Data Flow: Query Processing

```
User Query → ChatService.ask_question()
                ↓
        RAGPipeline.retrieve_context()
                ↓
        VectorStore.query() → Pinecone
                ↓
        Retrieved Chunks (top-k)
                ↓
        RAGPipeline.format_context()
                ↓
        LLM.generate_answer() → OpenAI/Gemini
                ↓
        Formatted Response with Sources
```

---

<a id="api-endpoints"></a>
## 📡 API Endpoints

### Document Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/files/upload` | POST | Upload single file with auto-ingestion |
| `/api/ingest/document` | POST | Process document by filename |
| `/api/ingest/bulk` | POST | Bulk process directory contents |

### Search & Chat

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/chat/search` | POST | Semantic document search (returns chunks) |
| `/api/chat/ask` | POST | Q&A with AI-generated answers |
| `/api/chat/ask/video` | POST | Video transcript-specific Q&A |
| `/api/chat/recommendations` | POST | Content recommendations based on query |
| `/api/chat/sessions` | GET/POST | Chat session management |

### Auth & Profile

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/config` | GET | Supabase config for client |
| `/api/auth/forgot-password` | POST | Password reset |
| `/api/profile/me` | GET/PATCH | User profile |

### Admin

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/users` | GET | List all users |
| `/api/admin/invite` | POST | Generate invitation code |
| `/api/admin/settings` | GET/PATCH | Admin settings |
| `/api/admin/ingestion/stats` | GET | Ingestion statistics |

### System

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/visibility/vector-store` | GET | Pinecone index statistics |

### Video Processing

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/videos/upload` | POST | Video upload with Whisper transcription |

### Interactive API Documentation

Visit [http://localhost:8000/docs](http://localhost:8000/docs) for interactive Swagger UI documentation with request/response schemas.

---

<a id="next-steps--improvements"></a>
## 🎯 Next Steps & Improvements

### Immediate Priorities

1. **Implement Authentication** 🔐
   - JWT-based auth system
   - Role-based access control
   - Secure API endpoints

2. **Add Chat History** 💬
   - Database schema for sessions/messages
   - Backend persistence
   - Frontend integration

3. **Enhance Security** 🛡️
   - Fix CORS configuration
   - Add rate limiting
   - Input validation and sanitization

### Medium-Term Enhancements

4. **Feedback System** ⭐
   - Rating collection
   - Analytics dashboard
   - ML training data export

5. **RAG Quality** 🧠
   - Hybrid search (vector + keyword)
   - Re-ranking models
   - Better chunking strategies

6. **Testing & Monitoring** 📊
   - Comprehensive test suite
   - Structured logging
   - Performance metrics

### Long-Term Goals

7. **CFC Integration** 🔗
   - Analytics platform integration
   - Usage metrics dashboard
   - Batch data exports

8. **Performance** ⚡
   - Redis caching
   - Async processing
   - Connection pooling

9. **Documentation** 📚
   - Architecture diagrams
   - API reference
   - Developer guides

---

<a id="support--contact"></a>
## 🆘 Support & Contact

### Questions or Issues?

**Contact**: **Dan Bates from CFC Tech**

**Important**: Dan Bates is your primary contact for:
- **Pinecone API credentials** (required)
- **Supabase credentials** (optional)
- Project architecture and design decisions
- Integration requirements
- Business logic and requirements
- Technical clarifications

For other credentials (OpenAI, Gemini), you can create your own accounts or ask Dan Bates if CFC has shared accounts.

### Additional Resources

- **Technical Handover**: See `HANDOVER_DOCUMENT.md` for comprehensive technical details
- **Setup Guide**: See `SETUP_GUIDE.md` for quick setup instructions
- **API Docs**: Visit `/docs` endpoint when server is running
- **Code Comments**: Most files have inline documentation

### Common Issues

**Pinecone Connection Errors**:
- **Contact Dan Bates** if you don't have the Pinecone API key
- Verify `PINECONE_API_KEY` is set correctly in `.env`
- Check index name matches the one provided by Dan Bates
- Ensure index dimension is `384`

**Document Processing Fails**:
- Ensure Office/LibreOffice is installed for `.doc` files
- Check file permissions in `data/documents/` directory
- Review logs for specific error messages

**No Search Results**:
- Verify documents have been ingested (`/api/visibility/vector-store`)
- Check Pinecone index has vectors
- Try different query keywords

**Frontend Not Loading**:
- Ensure server is running: `http://localhost:8000`
- Check browser console for errors
- Verify `CORS_ORIGINS` in `.env` includes your domain in production

---

<a id="project-status"></a>
## 📝 Project Status

**Current Version**: 1.0.0  
**Status**: Functional MVP - Ready for production enhancements  
**Last Updated**: See git commit history

### What's Working ✅

- Document ingestion and processing
- Vector search with Pinecone
- AI-powered Q&A
- Video transcription
- Web UI for uploads and chat
- Local and cloud storage options

### What Needs Work 🚧

- Authentication and authorization
- Chat history persistence
- Feedback collection system
- Enhanced RAG quality
- Comprehensive testing
- Production monitoring



---

**Built for CFC Technologies** 🚀

# 🚀 C# Migration Guide: Python (FastAPI) to .NET 8/9

This guide provides a roadmap for the engineering team responsible for migrating the CFC Chat-AI backend from Python to C#.

## 1. Architectural Mapping

| Feature | Python (Current) | C# / .NET (Recommended) |
|---------|------------------|-------------------------|
| **Web Framework** | FastAPI | ASP.NET Core Web API |
| **Orchestration** | Manual / `ChatService` | **Semantic Kernel** |
| **Vector DB** | `pinecone-client` | `Pinecone.NET` or Semantic Kernel Connector |
| **Database/Auth** | `supabase-py` | `supabase-csharp` |
| **Embeddings** | `sentence-transformers` | `Microsoft.ML.OnnxRuntime` (Local) or Azure OpenAI Embeddings |
| **LLM Interface** | `openai` SDK | `Microsoft.SemanticKernel.Connectors.OpenAI` |
| **Transcription** | `openai-whisper` | `Whisper.net` (C# wrapper for whisper.cpp) |
| **Document Parsing**| `python-docx` | `DocumentFormat.OpenXml` |
| **Background Tasks**| FastAPI `BackgroundTasks`| `IBackgroundTaskQueue` or `Hangfire` |

---

## 2. Core Logic Migration

### A. RAG Pipeline (`app/core/rag.py`)
In C#, use **Semantic Kernel**'s memory features.
- **Python:** `RAGPipeline.retrieve_context` manually queries Pinecone and then fetches text from Supabase.
- **C#:** Use `IMemoryStore` with the Pinecone connector. Consider implementing a "Hybrid Search" decorator that combines vector results with SQL full-text search.

### B. Embedding Logic (`app/core/embeddings.py`)
The current model is `all-MiniLM-L6-v2`.
- **Recommendation:** Do not run Python just for embeddings. Export the model to **ONNX** format and use `Microsoft.ML.OnnxRuntime` in C# to keep embeddings local and high-performance.

### C. Document Processing (`app/services/document_processor.py`)
This is the most complex part of the migration.
- **Chunking Strategy:** Use the `TextChunker` utility in Semantic Kernel.
- **Image Extraction:** Use `DocumentFormat.OpenXml` for Word docs.
- **OCR:** Use the `Tesseract` NuGet package if local OCR is required, or Azure AI Vision for better accuracy.

---

## 3. Database & Auth Strategy

The project uses Supabase. To maintain continuity:
1. **Supabase-csharp:** Use the official community SDK to interact with Auth, PostgREST, and Realtime.
2. **Superuser Protection:** The PostgreSQL trigger `protect_superuser_profile` in Supabase is backend-agnostic. It will work perfectly with C# as it resides in the database layer.
3. **Identity:** Replace the frontend-only role check with ASP.NET Core **Policy-based Authorization** using the JWTs issued by Supabase.

---

## 4. Deployment on Windows VM

The current VM uses **IIS** + **NSSM**.
- **Migration:** Switch to native **IIS hosting for ASP.NET Core**.
- **NSSM:** No longer needed for the web app, but could be used for the Whisper transcription worker if you run it as a separate background service.

---

## 5. Potential Pitfalls

- **Python-Specific Libraries:** `python-docx` and `PyMuPDF` have very specific parsing behaviors. Expect slight differences in text extraction; you may need to re-ingest documents to ensure consistency.
- **Whisper Performance:** `Whisper.net` is very fast but requires specific shared libraries (`whisper.dll`). Ensure these are included in your build artifacts.
- **Async/Await:** Ensure the entire C# pipeline is async-native to handle the high latency of LLM and Vector DB calls.

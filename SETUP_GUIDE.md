# ⚙️ Setup Guide

Follow these steps and you'll have the chatbot backend running in minutes.

## 1️⃣ Clone & Install
```bash
python -m venv .venv
source .venv/bin/activate   # macOS/Linux
# .venv\Scripts\activate    # Windows
pip install -r requirements.txt
```
Tip: keep the virtualenv around so future installs are instant. WE NEED THIS!

## 2️⃣ Configure Secrets
```bash
cp .env.example .env
```
Open `.env` and add:
- `PINECONE_API_KEY` – required for vector search.
- `SUPABASE_URL`, `SUPABASE_ANON_KEY`, `SUPABASE_SERVICE_ROLE_KEY` – required for auth and database.
- `GEMINI_API_KEY` – required for AI-generated answers.
- `CORS_ORIGINS` – set to your domain in production (e.g. `https://your-domain.com`).

## 3️⃣ Start the API
```bash
uvicorn main:app --reload
```
The app is now running at **[http://localhost:8000](http://localhost:8000)**.

- **Web UI**: [http://localhost:8000](http://localhost:8000) — log in and start chatting
- **API docs**: [http://localhost:8000/docs](http://localhost:8000/docs) — interactive Swagger UI
- **Health check**: [http://localhost:8000/api/health](http://localhost:8000/api/health)

## 4️⃣ Ingest Docs
Upload and ingest in one step (single file or via the admin UI):
```bash
curl -X POST "http://localhost:8000/api/files/upload" \
     -H "Content-Type: multipart/form-data" \
     -F "file=@your-file.docx"
```

Bulk upload:
```bash
curl -X POST "http://localhost:8000/api/files/upload" \
     -H "Content-Type: multipart/form-data" \
     -F "file=@doc1.docx"
```

After the request completes, check `data/processed/content_repository/<doc-slug>/` for readable section JSON files and any extracted images.

## 5️⃣ Search & Ask
- `/api/chat/search` — returns the best chunks with section/image paths.
- `/api/chat/ask` — returns the same context plus a friendly answer.
- `/api/visibility/vector-store` — shows how many vectors Pinecone currently stores.

## 🆘 Need Help?
- Conversion errors? Make sure `ffmpeg` is installed for video processing.
- Pinecone issues? Double-check `PINECONE_API_KEY` and region in `.env`.
- Auth issues? Verify your Supabase keys are correct.

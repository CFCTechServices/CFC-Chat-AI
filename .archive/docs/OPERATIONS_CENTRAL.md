# 🛠️ Operations Central: Setup, Deploy & Maintenance

This document is the "Single Source of Truth" for running and maintaining the CFC Chat-AI project.

---

## 1. Quick Start (Local Development)

### Prerequisites
- Python 3.11+
- [ffmpeg](https://ffmpeg.org/download.html) (for video transcription)
- [Tesseract OCR](https://github.com/UB-Mannheim/tesseract/wiki) (for scanned documents)

### Steps
1. **Clone & Virtualenv:**
   ```bash
   git clone <repo-url>
   python -m venv .venv
   source .venv/bin/activate  # or .venv\Scripts\activate on Windows
   pip install -r requirements.txt
   ```
2. **Environment Configuration:**
   - Copy `.env.example` to `.env`.
   - Ensure `PINECONE_API_KEY`, `SUPABASE_URL`, and `AZURE_OPENAI_API_KEY` are set.
3. **Run Backend:**
   ```bash
   uvicorn main:app --reload
   ```
4. **Access UI:** `http://localhost:8000/ui`

---

## 2. Windows VM Deployment (Azure)

The application runs on a Windows 10 Pro Azure VM using **IIS** (reverse proxy) and **NSSM** (service manager).

### One-Time Admin Setup
See `ADMIN_SETUP_GUIDE.md` for full instructions on installing:
- Python, Git, ffmpeg, Tesseract, Poppler, NSSM, IIS (with ARR Proxy).

### Deploying Updates
From an Administrator PowerShell terminal on the VM:
```powershell
cd C:\cfcchat
git pull
.\deploy-windows.ps1
```

---

## 3. Maintenance & Troubleshooting

### Restarting Services
If the backend is unresponsive:
```powershell
nssm restart CFC-ChatAI
```

### Viewing Logs
- **App Output:** `C:\cfcchat\logs\stdout.log`
- **Errors:** `C:\cfcchat\logs\stderr.log`

### Health Check
Run locally or from the VM:
```bash
curl http://127.0.0.1:8000/api/health
```

---

## 4. Secrets & Contacts

| Secret Type | Purpose | Owner/Contact |
|-------------|---------|---------------|
| **Pinecone**| Vector Database | Dan Bates (CFC Tech) |
| **Supabase**| Auth/DB/Storage | Dan Bates (CFC Tech) |
| **Azure AI**| LLM (GPT-4o) | Dan Bates (CFC Tech) |

---

## 5. Directory Structure Map

- `/app`: Backend logic (FastAPI)
- `/web`: Frontend logic (React/Vite)
- `/tests`: Pytest suite
- `/scripts`: Diagnostic tools for ingestion
- `/docs`: Technical guides and migration plans

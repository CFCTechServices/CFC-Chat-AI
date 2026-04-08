# CFC Chat-AI — Deployment Guide

This document covers the production deployment of CFC Chat-AI on the Windows Azure VM.
The application runs natively as a **Python Windows Service** (via NSSM) with **IIS** as the reverse proxy.

> **Prerequisites (one-time admin setup):** See `ADMIN_SETUP_GUIDE.md` for all software that must be installed on the VM before running this guide.

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [VM Specifications](#2-vm-specifications)
3. [Environment Variables](#3-environment-variables)
4. [First-Time Deployment](#4-first-time-deployment)
5. [Updating the App](#5-updating-the-app)
6. [Operations & Maintenance](#6-operations--maintenance)
7. [Domain & HTTPS Setup](#7-domain--https-setup)

---

## 1. Architecture

```
Internet
   │
   ▼
[IIS :80/:443]  ←── Frontend static files from C:\inetpub\cfcchat\ui\
   │
   │  /api/*  (ARR reverse proxy)
   ▼
[uvicorn :8000]  ←── Managed by NSSM Windows Service "CFC-ChatAI"
[FastAPI application]
   │
   ├──► [Pinecone]               Vector database (document + video chunk search)
   ├──► [Supabase]               PostgreSQL (sessions, users, profiles) + Storage (files)
   ├──► [Azure OpenAI]           LLM for answer generation
   ├──► [sentence-transformers]  Local embedding model (CPU)
   └──► [openai-whisper]         Local transcription model (CPU, on-demand)
```

**Why NSSM instead of Docker:** The Azure VM does not support nested virtualization, which Docker Desktop requires on Windows. The application runs natively with no containers needed.

---

## 2. VM Specifications

| Spec | Minimum | Recommended |
|------|---------|-------------|
| **OS** | Windows 10 Pro | Windows 10 Pro |
| **vCPUs** | 2 | 4 |
| **RAM** | 8 GB | 16 GB |
| **Disk** | 60 GB | 100 GB |

**Why these specs:**
- Python ML packages (torch, sentence-transformers, whisper) require ~2 GB disk
- HuggingFace model cache adds ~400 MB
- The embedding model holds ~300 MB in RAM at runtime
- Whisper uses ~1 GB RAM during active transcription

---

## 3. Environment Variables

Copy `.env.example` to `.env` and fill in all values. Never commit `.env` to version control.

| Variable | Required | Description |
|---|---|---|
| `SUPABASE_URL` | Yes | Your Supabase project URL |
| `SUPABASE_KEY` | Yes | Supabase **service role** key (not anon key) |
| `SUPABASE_BUCKET_DOCUMENTS` | Yes | Storage bucket name for documents |
| `SUPABASE_BUCKET_VIDEOS` | Yes | Storage bucket name for videos |
| `PINECONE_API_KEY` | Yes | Pinecone API key |
| `PINECONE_INDEX_NAME` | Yes | Pinecone index for document chunks |
| `PINECONE_VIDEO_INDEX_NAME` | Yes | Pinecone index for video transcript chunks |
| `AZURE_OPENAI_API_KEY` | Yes | Azure OpenAI resource key |
| `AZURE_OPENAI_ENDPOINT` | Yes | Azure OpenAI resource endpoint (e.g. `https://my-resource.openai.azure.com/`) |
| `AZURE_OPENAI_DEPLOYMENT` | Yes | Deployment name inside the resource (e.g. `gpt-4o-mini`) |
| `AZURE_OPENAI_API_VERSION` | Yes | API version (e.g. `2024-08-01-preview`) |
| `CORS_ORIGINS` | Yes | Comma-separated allowed origins (e.g. `http://192.168.201.211`) |
| `FRONTEND_BASE_URL` | Yes | The public-facing base URL of the app |
| `ADMIN_SECRET` | Yes | Secret key for admin operations |
| `API_HOST` | No | Host to bind to (default: `0.0.0.0`) |
| `API_PORT` | No | Port to bind to (default: `8000`) |

> If `AZURE_OPENAI_DEPLOYMENT` points to a deprecated model, the chatbot will fall back to raw chunk summaries without LLM synthesis. Update the deployment name in `.env` and restart the service.

---

## 4. First-Time Deployment

### Step 1: Clone the Repository

```powershell
# Run as Administrator
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
cd C:\
git clone <repository-url> cfcchat
cd cfcchat
git config --global --add safe.directory C:/cfcchat
```

### Step 2: Configure Environment Variables

```powershell
Copy-Item .env.example .env
notepad .env
# Fill in all required values from Section 3
```

### Step 3: Run the Deploy Script

```powershell
# From C:\cfcchat, run as Administrator
.\deploy-windows.ps1
```

This script:
1. Creates/updates the Python virtual environment in `.venv\`
2. Installs CPU-only PyTorch (avoids the 2.5 GB CUDA download)
3. Installs all application dependencies from `requirements.txt`
4. Copies frontend files to `C:\inetpub\cfcchat\ui\`
5. Copies `deployment\iis-web.config` to `C:\inetpub\cfcchat\web.config`
6. Installs and starts the **CFC-ChatAI** Windows Service via NSSM
7. Runs a health check to confirm the backend is responding

> First run takes **10–20 minutes** to download ML packages (~2 GB).

### Step 4: Configure IIS Site

1. Open **IIS Manager**
2. Right-click **Sites → Add Website**:
   - Site name: `CFC Chat AI`
   - Physical path: `C:\inetpub\cfcchat`
   - Binding: Port `80`
3. The `web.config` was already copied by the deploy script in Step 3

### Step 5: Verify

```powershell
nssm status CFC-ChatAI                              # SERVICE_RUNNING
Invoke-RestMethod http://127.0.0.1:8000/api/health  # returns JSON
# Browser: http://192.168.201.211/ui/               # shows login page
```

---

## 5. Updating the App

Every time code is pushed to the repository:

```powershell
cd C:\cfcchat
git pull
.\deploy-windows.ps1
```

The deploy script automatically stops the old service, force-kills any stale Python processes on port 8000, deploys new frontend files, and restarts the service with the new code.

> **After editing `.env`:** You must re-run `.\deploy-windows.ps1` (or at minimum `nssm stop CFC-ChatAI` + kill stale PIDs + `nssm start CFC-ChatAI`) for the new values to take effect. The Python process loads env vars once at startup.

---

## 6. Operations & Maintenance

### Service Management

```powershell
nssm status CFC-ChatAI      # Check status
nssm stop   CFC-ChatAI      # Stop
nssm start  CFC-ChatAI      # Start
```

### Viewing Logs

```powershell
Get-Content C:\cfcchat\logs\stderr.log -Tail 50    # Application errors
Get-Content C:\cfcchat\logs\stdout.log -Tail 50    # Application output
```

### Checking for Stale Processes

If the service appears stuck (deploy ran but old code is still serving), check:

```powershell
Get-Process -Name python | Select-Object Id, StartTime
netstat -ano | findstr ":8000"
```

If the `StartTime` is older than your last deploy, force-kill:

```powershell
Stop-Process -Id <PID> -Force
nssm start CFC-ChatAI
```

### Disk Usage

```powershell
Get-PSDrive C | Select-Object Used, Free             # Overall disk
Get-ChildItem C:\cfcchat -Directory | ForEach-Object { 
    "{0,10} MB  {1}" -f ([math]::Round((Get-ChildItem $_.FullName -Recurse -ErrorAction SilentlyContinue | 
    Measure-Object -Property Length -Sum).Sum / 1MB, 1)), $_.Name 
}
```

### Azure OpenAI Model Deprecation

Microsoft periodically retires older models. If you see `410 ModelDeprecated` in the logs:

1. Go to **Azure OpenAI Studio** → [oai.azure.com](https://oai.azure.com)
2. **Deployments → Create new deployment** → choose a current model (e.g. `gpt-4o-mini`)
3. Update `AZURE_OPENAI_DEPLOYMENT=<new-name>` in `C:\cfcchat\.env`
4. Re-run `.\deploy-windows.ps1`

---

## 7. Domain & HTTPS Setup

When a public domain (e.g. `chat.cfctech.com`) is ready:

### Client IT Team

1. **DNS:** Create an A record pointing the domain to the VM's public IP (Azure Portal → VM → Networking)
2. **Firewall:** Add Azure NSG inbound rules for ports `80` and `443` from source `Any`
3. **SSL:** Install [Win-ACME](https://www.win-acme.com/) on the VM and run as Administrator — it automatically issues and renews a Let's Encrypt certificate for IIS

### Development Team

```powershell
# On the VM, update .env:
notepad C:\cfcchat\.env
# Change:
#   CORS_ORIGINS=https://chat.cfctech.com
#   FRONTEND_BASE_URL=https://chat.cfctech.com

.\deploy-windows.ps1
```

Update **Supabase → Authentication → URL Configuration**:
- **Site URL**: `https://chat.cfctech.com`
- **Redirect URLs**: add `https://chat.cfctech.com/*`

Update `TEAM_ACCESS_GUIDE.md` with the new public URL.

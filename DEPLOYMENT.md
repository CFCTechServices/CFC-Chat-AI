# CFC Chat-AI — Deployment Guide & Technical Justification

This document records the full deployment journey for the CFC Chat-AI application: what was attempted, why certain approaches failed, what the production infrastructure requirements are, and how to set the system up correctly. It is intended for both the engineering team and the client as a reference and justification document.

---

## Table of Contents

1. [Application Overview](#1-application-overview)
2. [Technology Stack & Why It Demands Resources](#2-technology-stack--why-it-demands-resources)
3. [What Was Attempted — Chronological Record](#3-what-was-attempted--chronological-record)
4. [Why Docker Failed on the Current VM](#4-why-docker-failed-on-the-current-vm)
5. [Production Infrastructure Requirements](#5-production-infrastructure-requirements)
6. [Deployment Option A — Docker + Docker Compose (Recommended for Production)](#6-deployment-option-a--docker--docker-compose-recommended-for-production)
7. [Deployment Option B — Direct venv + systemd (No Docker)](#7-deployment-option-b--direct-venv--systemd-no-docker)
8. [Nginx Configuration](#8-nginx-configuration)
9. [Environment Variables Reference](#9-environment-variables-reference)
10. [Maintenance & Operations](#10-maintenance--operations)
11. [Architecture Diagram](#11-architecture-diagram)

---

## 1. Application Overview

CFC Chat-AI is an AI-powered RAG (Retrieval-Augmented Generation) chatbot for CFC animal feed software documentation. Users can ask questions in natural language; the system retrieves relevant documentation chunks from a vector database and generates grounded answers using a large language model.

**Frontend:** React 18 served as static files (no npm build step). HTML/JSX loaded directly in the browser via CDN imports.

**Backend:** Python FastAPI application providing a REST API, with:
- Document ingestion and chunking
- Semantic search via Pinecone vector database
- LLM answer generation via Google Gemini
- Video upload and transcription via OpenAI Whisper
- User authentication via Supabase Auth
- Session and profile management via Supabase PostgreSQL

**Entry point:** `main.py` → Gunicorn (production WSGI server) → Uvicorn workers

---

## 2. Technology Stack & Why It Demands Resources

Understanding why this application needs more disk space and RAM than a typical web app is critical for provisioning the right infrastructure.

### ML Model Downloads at Runtime

The application uses two locally-running ML models that are downloaded on first start and cached to disk:

| Model | Purpose | Download Size | RAM Usage |
|---|---|---|---|
| `sentence-transformers` (all-MiniLM-L6-v2) | Converts text to vector embeddings for semantic search | ~90 MB | ~300 MB |
| `openai-whisper` (base model) | Transcribes uploaded video files to text | ~140 MB | ~1 GB (during transcription) |

These models are downloaded from HuggingFace Hub the first time the application starts. They are cached to `~/.cache/huggingface/` (or the Docker volume `hf_cache`).

### Python ML Dependencies

Installing the ML stack requires compiling C extensions and downloading large binary wheels:

| Package | Installed Size | Why It's Needed |
|---|---|---|
| `torch` (CPU-only) | ~750 MB on disk | Required by `sentence-transformers` and `openai-whisper` |
| `sentence-transformers` | ~50 MB + torch | Embedding model library |
| `openai-whisper` | ~30 MB + torch | Video transcription |
| `numba` / `llvmlite` | ~500 MB | Dependency of `openai-whisper` via `numba` |
| Other dependencies | ~200 MB | FastAPI, Supabase, Pinecone, etc. |

**Total installed Python environment: approximately 1.5–2 GB**

### Why CUDA Torch Was a Problem

By default, `sentence-transformers` and `openai-whisper` will install `torch` with CUDA support (GPU). The CUDA build of PyTorch is **~2.5 GB** — ten times larger than the CPU-only build (~250 MB). Since this application runs on a CPU-only VM, we explicitly install the CPU-only torch wheel first to prevent the CUDA version from being pulled in.

```
pip install torch --index-url https://download.pytorch.org/whl/cpu
```

This is already configured in the `Dockerfile`.

---

## 3. What Was Attempted — Chronological Record

### 3.1 Initial State

The repository existed with a working local development setup but no production deployment configuration. Issues identified:

- CORS was open to all origins (`"*"`) — a security risk
- Several API route groups were not under the `/api` prefix, causing mismatches with the frontend
- Frontend JavaScript files were calling wrong API paths (e.g., `/files/upload` instead of `/api/files/upload`)
- No `Dockerfile`, `docker-compose.yml`, or production WSGI server configured
- Configuration used a plain Python class instead of environment-variable-backed `pydantic-settings`
- Windows-only package (`pywin32`) was in `requirements.txt`
- Unused email/Resend integration still in the codebase

### 3.2 Codebase Fixes Applied

Before attempting deployment, the following fixes were made:

- **Config:** Refactored `app/config.py` to use `pydantic-settings` `BaseSettings` so all settings are read from environment variables / `.env` file
- **CORS:** Changed from wildcard to `settings.CORS_ORIGINS` (comma-separated env var)
- **Route prefixes:** All API routes now correctly under `/api/*`
- **Frontend paths:** Fixed 5 frontend files calling wrong API paths
- **Dependencies:** Removed `pywin32`, removed Resend/email packages, added `gunicorn` and `pydantic-settings`
- **Dead code:** Deleted `app/services/email_service.py` and `docs/EMAIL_SETUP.md`
- **Docker files:** Created `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `deploy.sh`, `deployment/nginx.conf.example`, `.env.example`

### 3.3 Docker Build Attempt 1 — CUDA Torch Disk Overflow

**What happened:** Running `docker build` on the Azure VM triggered a full CUDA PyTorch install (~2.5 GB) because `sentence-transformers` requested it as a dependency.

**Error:** Disk full during `pip install sentence-transformers`

**Fix:** Modified the `Dockerfile` to install CPU-only torch explicitly before running `requirements.txt`:
```dockerfile
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu \
    && pip install --no-cache-dir -r requirements.txt
```

### 3.4 Docker Build Attempt 2 — llvmlite Disk Overflow

**What happened:** The CPU-only torch fix worked for `sentence-transformers`, but `openai-whisper` pulls in `numba` as a dependency, which in turn pulls in `llvmlite`. The `llvmlite` package includes a large compiled shared library (`libllvmlite.so`, ~500 MB).

**Error:** `OSError: [Errno 28] No space left on device` while downloading/installing `llvmlite`

**Attempted fix:** Removed `openai-whisper` from `requirements.txt` and commented out the videos router in `main.py`.

> **Note:** This was a temporary diagnostic step. The whisper/videos functionality has since been restored. `openai-whisper` is back in `requirements.txt`.

### 3.5 Docker Build Attempt 3 — Image Export Disk Overflow

**What happened:** Even after removing `openai-whisper`, the Docker build completed pip installations successfully but failed at the final image export stage — when Docker writes the finished image layers to the containerd image store.

**Error:** `failed to export image: failed to create ... torch/testing/_comparison.py: write /var/lib/docker/...`

**Root cause:** The Docker image (containing Python 3.11-slim + torch + sentence-transformers + all other packages) was approximately 2–2.5 GB. Docker's image export process requires temporary free space in addition to the final image size. The VM's OS disk had insufficient space for this operation.

**Conclusion:** Docker is not viable on a VM with a small OS disk, regardless of optimisations to the image.

### 3.6 Direct venv Attempt — Disk Overflow

**What happened:** To bypass Docker's overhead, a direct Python virtual environment approach was attempted:

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install torch --index-url https://download.pytorch.org/whl/cpu
pip install -r requirements.txt
```

**Error:** `OSError: [Errno 28] No space left on device` during `sentence-transformers` install

**Root cause:** Even without Docker, the Python packages alone (~1.5–2 GB) exceeded the available disk space on the VM. The OS disk was not large enough to hold the application's dependencies regardless of the deployment method.

**Conclusion:** The VM must be provisioned with a larger disk before any deployment method will succeed.

---

## 4. Why Docker Failed on the Current VM

Docker has two disk space requirements:
1. **Build-time space:** Space needed to download, compile, and assemble the image layers
2. **Export space:** Temporary space required while Docker writes the final image to its internal store

The minimum disk consumption for a successful build is approximately:

| Stage | Space Required |
|---|---|
| Base Python 3.11-slim image | ~150 MB |
| CPU-only PyTorch | ~750 MB installed |
| sentence-transformers + deps | ~200 MB installed |
| openai-whisper + numba + llvmlite | ~700 MB installed |
| Other Python packages | ~200 MB installed |
| Docker layer cache (during build) | ~1–2× the above as temp space |
| Final exported image | ~2–2.5 GB |
| **Total disk needed** | **~5–6 GB free minimum** |

The Azure VM's OS disk had insufficient free space for this. The fix is to provision a VM with a larger disk.

---

## 5. Production Infrastructure Requirements

### Minimum Specification

| Resource | Minimum | Recommended |
|---|---|---|
| **OS Disk** | 40 GB | 64 GB |
| **RAM** | 4 GB | 8 GB |
| **CPU** | 2 vCPUs | 4 vCPUs |
| **OS** | Ubuntu 22.04 LTS | Ubuntu 22.04 LTS |
| **Open Ports** | 80 (HTTP), 443 (HTTPS), 22 (SSH) | Same |

### Why These Specifications

**Disk (40 GB minimum):**
- OS + system packages: ~5 GB
- Docker installation: ~1 GB
- Docker image (app): ~2.5 GB
- Docker layer cache during builds: ~3 GB
- HuggingFace model cache (whisper + embeddings): ~400 MB
- Application data (`data/` directory — uploaded documents, transcripts, processed files): ~5–10 GB estimated
- Operating headroom (logs, temp files, OS updates): ~5 GB
- **Total: ~18–27 GB for a healthy system; 40 GB provides comfortable headroom**

**RAM (4 GB minimum):**
- sentence-transformers embedding model: ~300 MB
- openai-whisper during active transcription: ~1 GB peak
- FastAPI/Gunicorn with 2 workers: ~500 MB
- OS and other processes: ~500 MB
- **Total: ~2.5 GB active; 4 GB minimum with 8 GB recommended for concurrent transcription requests**

**CPU (2 vCPUs):**
- Gunicorn runs 2 worker processes by default
- Whisper transcription and embedding inference are CPU-intensive but not time-critical
- I/O-bound chat requests benefit more from async than extra CPUs

### Azure VM Size Recommendations

| Azure Size | vCPUs | RAM | OS Disk | Suitable? |
|---|---|---|---|---|
| B1s | 1 | 1 GB | 30 GB | No — insufficient RAM and disk |
| B2s | 2 | 4 GB | 30 GB | No — disk too small |
| B2ms | 2 | 8 GB | 32 GB | Borderline — expand disk to 64 GB |
| B4ms | 4 | 16 GB | 32 GB | Yes — expand disk to 64 GB |
| D2s_v3 | 2 | 8 GB | 50 GB | Yes — preferred minimum |
| D4s_v3 | 4 | 16 GB | 50 GB | Yes — comfortable for production |

To expand an Azure VM disk: **Azure Portal → Virtual Machine → Disks → Resize**. This requires the VM to be stopped and deallocated first.

---

## 6. Deployment Option A — Docker + Docker Compose (Recommended for Production)

Docker provides isolation, reproducibility, and easy updates. Use this option once the VM has adequate disk space.

### Prerequisites

```bash
# Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
# Log out and back in for group change to take effect

# Install Docker Compose plugin
sudo apt-get install docker-compose-plugin

# Install nginx
sudo apt-get install nginx
```

### Step 1: Clone the Repository

```bash
git clone <repository-url> /opt/cfcchat
cd /opt/cfcchat
```

### Step 2: Configure Environment Variables

```bash
cp .env.example .env
nano .env
# Fill in all required values (see Section 9)
```

### Step 3: Deploy Frontend Static Files

```bash
sudo mkdir -p /var/www/cfcchat
sudo cp -r web/* /var/www/cfcchat/
sudo chown -R www-data:www-data /var/www/cfcchat
```

### Step 4: Configure Nginx

```bash
sudo cp deployment/nginx.conf.example /etc/nginx/sites-available/cfcchat
# Edit the server_name to match your domain or public IP
sudo nano /etc/nginx/sites-available/cfcchat
sudo ln -s /etc/nginx/sites-available/cfcchat /etc/nginx/sites-enabled/cfcchat
sudo nginx -t
sudo systemctl reload nginx
```

### Step 5: Build and Start the Application

```bash
# This will take 10–20 minutes on first run (downloading models and packages)
docker compose up -d --build
```

### Step 6: Verify

```bash
docker compose ps          # should show backend as "Up"
docker compose logs -f     # watch startup logs
curl http://localhost:8000/api/health
```

### Updating the Application

```bash
cd /opt/cfcchat
git pull
# If frontend files changed:
sudo cp -r web/* /var/www/cfcchat/
# Rebuild and restart:
docker compose up -d --build
```

Or use the included script:
```bash
bash deploy.sh
```

### How Docker Compose Is Configured

The `docker-compose.yml` file defines:
- **backend** service: the FastAPI application, bound to `127.0.0.1:8000` (not exposed publicly; nginx proxies to it)
- **`./data:/app/data`** volume mount: persists uploaded files and processed content across container restarts
- **`hf_cache`** named volume: persists downloaded HuggingFace models (~400 MB) across image rebuilds so they are not re-downloaded every time

---

## 7. Deployment Option B — Direct venv + systemd (No Docker)

Use this option if Docker is not available or the VM disk is too small for Docker's overhead (~3 GB extra for layer cache). This approach installs packages directly on the host and uses systemd to keep the process running.

### Prerequisites

```bash
sudo apt-get update
sudo apt-get install -y python3 python3-venv python3-pip ffmpeg nginx git
```

### Step 1: Clone the Repository

```bash
git clone <repository-url> /opt/cfcchat
cd /opt/cfcchat
```

### Step 2: Configure Environment Variables

```bash
cp .env.example .env
nano .env
```

### Step 3: Create Virtual Environment and Install Dependencies

```bash
cd /opt/cfcchat
python3 -m venv .venv
source .venv/bin/activate

# Install CPU-only torch first to avoid the 2.5 GB CUDA build
pip install torch --index-url https://download.pytorch.org/whl/cpu

# Install remaining requirements
pip install -r requirements.txt
```

> This step will take 10–20 minutes and download approximately 1.5–2 GB of packages.

### Step 4: Deploy Frontend Static Files

```bash
sudo mkdir -p /var/www/cfcchat
sudo cp -r web/* /var/www/cfcchat/
sudo chown -R www-data:www-data /var/www/cfcchat
```

### Step 5: Configure Nginx

```bash
sudo cp deployment/nginx.conf.example /etc/nginx/sites-available/cfcchat
sudo nano /etc/nginx/sites-available/cfcchat   # set server_name
sudo ln -s /etc/nginx/sites-available/cfcchat /etc/nginx/sites-enabled/cfcchat
sudo nginx -t && sudo systemctl reload nginx
```

### Step 6: Create a systemd Service

```bash
sudo nano /etc/systemd/system/cfcchat.service
```

Paste the following (adjust paths if needed):

```ini
[Unit]
Description=CFC Chat-AI FastAPI Application
After=network.target

[Service]
User=www-data
Group=www-data
WorkingDirectory=/opt/cfcchat
EnvironmentFile=/opt/cfcchat/.env
ExecStart=/opt/cfcchat/.venv/bin/gunicorn main:app \
    --workers 2 \
    --worker-class uvicorn.workers.UvicornWorker \
    --bind 127.0.0.1:8000 \
    --timeout 120 \
    --log-level info
Restart=on-failure
RestartSec=5s

[Install]
WantedBy=multi-user.target
```

### Step 7: Enable and Start the Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable cfcchat
sudo systemctl start cfcchat
sudo systemctl status cfcchat
```

### Updating the Application (venv approach)

```bash
cd /opt/cfcchat
git pull
source .venv/bin/activate
pip install -r requirements.txt   # only if requirements changed
sudo cp -r web/* /var/www/cfcchat/
sudo systemctl restart cfcchat
```

---

## 8. Nginx Configuration

The included `deployment/nginx.conf.example` sets up nginx as a reverse proxy. Key behaviours:

- **`/api/`** — proxied to the FastAPI backend at `127.0.0.1:8000`
- **`/`** — frontend static files served from `/var/www/cfcchat/`
- **SPA fallback** — any path that doesn't match a static file falls back to `index.html` so React client-side routing works on page reload
- **File upload size** — `client_max_body_size 500M` to accommodate video file uploads

```nginx
server {
    listen 80;
    server_name your-domain-or-ip;

    client_max_body_size 500M;

    # API — proxy to FastAPI backend
    location /api/ {
        proxy_pass http://127.0.0.1:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_read_timeout 120s;
    }

    # Frontend — React SPA static files
    location / {
        root /var/www/cfcchat;
        try_files $uri $uri/ /index.html;
    }
}
```

For HTTPS, install Certbot:
```bash
sudo apt-get install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

---

## 9. Environment Variables Reference

Copy `.env.example` to `.env` and fill in all values. Never commit `.env` to version control.

| Variable | Required | Description |
|---|---|---|
| `SUPABASE_URL` | Yes | Your Supabase project URL |
| `SUPABASE_KEY` | Yes | Supabase service role key (not anon key) |
| `SUPABASE_BUCKET_DOCUMENTS` | Yes | Supabase Storage bucket name for documents |
| `SUPABASE_BUCKET_VIDEOS` | Yes | Supabase Storage bucket name for videos |
| `PINECONE_API_KEY` | Yes | Pinecone API key |
| `PINECONE_INDEX_NAME` | Yes | Pinecone index name for document chunks |
| `PINECONE_VIDEO_INDEX_NAME` | Yes | Pinecone index name for video transcript chunks |
| `GOOGLE_API_KEY` | Yes | Google Gemini API key |
| `CORS_ORIGINS` | Yes | Comma-separated list of allowed frontend origins (e.g., `https://yourdomain.com`) |
| `API_HOST` | No | Host to bind to (default: `0.0.0.0`) |
| `API_PORT` | No | Port to bind to (default: `8000`) |
| `ADMIN_SECRET` | Yes | Secret key for admin operations |

---

## 10. Maintenance & Operations

### Checking Application Health

```bash
# Docker deployment
docker compose ps
docker compose logs --tail=50 backend

# systemd deployment
sudo systemctl status cfcchat
sudo journalctl -u cfcchat -n 50
```

### Checking Disk Usage

```bash
df -h                          # overall disk usage
du -sh /opt/cfcchat/data/      # application data
du -sh ~/.cache/huggingface/   # model cache (venv deployment)
docker system df               # Docker image and volume usage
```

### Freeing Disk Space (Docker)

```bash
# Remove unused images and build cache
docker system prune -f

# Remove old images (keep the current one)
docker image prune -f
```

### Backing Up Application Data

The `data/` directory contains uploaded documents, video transcripts, and processed content. Back this up regularly:

```bash
tar -czf cfcchat-data-$(date +%Y%m%d).tar.gz /opt/cfcchat/data/
```

### Whisper Model — First-Run Download

On first startup, OpenAI Whisper will download the `base` model (~140 MB). This happens automatically and is logged to stdout. Subsequent starts use the cached model. In Docker, the `hf_cache` named volume persists this across container rebuilds.

---

## 11. Architecture Diagram

```
Internet
   │
   ▼
[Nginx :80/:443]  ←── Frontend static files from /var/www/cfcchat
   │
   │  /api/*
   ▼
[Gunicorn :8000]
[2× Uvicorn workers]
[FastAPI application]
   │
   ├──► [Pinecone]          Vector database (document + video chunk search)
   ├──► [Supabase]          PostgreSQL (sessions, users, profiles) + Storage (files)
   ├──► [Google Gemini]     LLM for answer generation
   ├──► [sentence-transformers]  Local embedding model (CPU)
   └──► [openai-whisper]    Local transcription model (CPU, on-demand)

Data persistence:
   [./data/]          Uploaded files, transcripts, processed content (bind mount / host dir)
   [hf_cache volume]  Downloaded ML models (~400 MB, survives image rebuilds)
```

---

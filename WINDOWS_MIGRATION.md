# Linux → Windows Hosting — Change Summary

This document summarizes all changes made to prepare the CFC Chat-AI application for hosting on a Windows VM instead of the originally planned Ubuntu VM.

---

## What Did NOT Change

The application code is entirely cross-platform. **No Python or JavaScript files were modified** for the Windows migration:

- `main.py` — unchanged
- `app/**/*.py` — unchanged (all 42 Python files)
- `web/**` — unchanged (all React/JSX frontend files)
- `Dockerfile` — unchanged (runs Linux inside Docker regardless of host OS)
- `docker-compose.yml` — unchanged (Docker Desktop handles path translation)
- `requirements.txt` — unchanged
- `.env` / `.env.example` — unchanged

---

## New Files Created

### 1. `deploy.ps1` — PowerShell Deployment Script
**Replaces:** `deploy.sh` (bash — still available for Linux use)

| Feature | `deploy.sh` (Linux) | `deploy.ps1` (Windows) |
|---------|---------------------|------------------------|
| Shell | Bash | PowerShell |
| Web root default | `/var/www/cfcchat` | `C:\inetpub\cfcchat` |
| Copy command | `sudo cp -r` | `Copy-Item -Recurse` |
| Docker commands | Same | Same |
| Pre-flight checks | None | Docker running + `.env` exists |

### 2. `deployment/iis-web.config` — IIS Reverse Proxy Config
**Replaces:** `deployment/nginx.conf.example` (still available for Linux use)

| Feature | Nginx Config | IIS web.config |
|---------|-------------|----------------|
| Reverse proxy `/api/*` | `proxy_pass http://127.0.0.1:8000` | ARR rewrite to `http://127.0.0.1:8000` |
| SPA catch-all | `try_files $uri /index.html` | URL Rewrite rule → `index.html` |
| Upload limit | `client_max_body_size 500M` | `maxAllowedContentLength 524288000` |
| Proxy timeout | `proxy_read_timeout 300s` | `<proxy timeout="00:05:00" />` |
| JSX MIME type | Not needed (nginx serves as-is) | Added `.jsx → text/jsx` mapping |

---

## Modified Files

### 3. `VM_REQUIREMENTS.md` — Updated for Windows
**Key changes from the Linux version:**

| Spec | Linux | Windows |
|------|-------|---------|
| OS | Ubuntu 22.04 LTS | Windows 10 Pro |
| RAM minimum | 4 GB | 8 GB (Docker Desktop + WSL2 overhead) |
| Disk minimum | 40 GB | 60 GB (WSL2 disk image + Docker layers) |
| Reverse proxy | Nginx | IIS + URL Rewrite + ARR |
| Access | SSH | RDP |

**New pre-install requirements for the client:**
- WSL2 (`wsl --install`)
- Docker Desktop (WSL2 backend)
- IIS with URL Rewrite and ARR modules
- Git for Windows

### 4. `DEPLOYMENT.md` — Added Section 11
Added a complete Windows deployment section with:
- Software prerequisites table
- Step-by-step IIS ARR proxy setup
- PowerShell deployment commands
- Windows maintenance commands
- Linux vs Windows comparison table
- Updated architecture diagram showing both Nginx and IIS

---

## Architecture Comparison

```
LINUX                                    WINDOWS
─────                                    ───────
Nginx (:80/:443)                         IIS (:80/:443) + ARR
  │                                        │
  ├─ /ui/* → /var/www/cfcchat             ├─ /ui/* → C:\inetpub\cfcchat
  ├─ /api/* → proxy 127.0.0.1:8000       ├─ /api/* → ARR proxy 127.0.0.1:8000
  └─ /* → index.html (SPA)               └─ /* → URL Rewrite → index.html
         │                                       │
    Docker (native)                         Docker Desktop (WSL2)
    └─ Linux container :8000               └─ Same Linux container :8000
       └─ gunicorn + uvicorn                  └─ gunicorn + uvicorn
          └─ FastAPI (identical)                 └─ FastAPI (identical)
```

---

## File Inventory

| File | Status | Purpose |
|------|--------|---------|
| `deploy.ps1` | **NEW** | PowerShell deploy script for Windows |
| `deployment/iis-web.config` | **NEW** | IIS reverse proxy + SPA config |
| `VM_REQUIREMENTS.md` | **MODIFIED** | Updated specs and install list for Windows |
| `DEPLOYMENT.md` | **MODIFIED** | Added Section 11 (Windows deployment) |
| `deploy.sh` | Unchanged | Linux deploy script (kept for reference) |
| `deployment/nginx.conf.example` | Unchanged | Linux Nginx config (kept for reference) |
| `Dockerfile` | Unchanged | Same Linux container image |
| All `.py` and `.jsx` files | Unchanged | No application code changes |

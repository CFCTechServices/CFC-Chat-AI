# Docker Desktop Failure — Root Cause & No-Docker Fallback Plan

## Why Docker Desktop Failed

Docker Desktop on Windows requires **hardware-level virtualization** to run Linux containers. On this VM, that is not available for the following reasons:

### Root Cause: Nested Virtualization Not Supported

This VM is itself running **inside a hypervisor** (Azure's Hyper-V infrastructure). Docker Desktop needs to spin up its own Hyper-V VM (or WSL2 VM) to run Linux containers — that is, a **VM inside a VM**. This is called **nested virtualization**.

**Not all VM sizes in Azure support nested virtualization.** The symptoms match exactly:
- "Virtualization support not detected" — Docker can't access hardware VT-x/AMD-V
- Engine hangs indefinitely at "Starting the Docker Engine..." — WSL2 VM never boots
- Cannot kill Docker Desktop from the taskbar — the engine process is stuck waiting on a hypervisor call that never returns

### Why Reinstalling Won't Help

The problem is not the Docker Desktop software — it is the **VM hardware configuration**. Reinstalling Docker Desktop would produce the same result because the underlying virtualization capability is absent. The virtualization features (Hyper-V, VirtualMachinePlatform) were successfully enabled in Windows, but Azure is not exposing the nested virtualization hardware feature to this VM size/configuration.

---

## Fallback Plan: No-Docker — Python Directly on Windows + IIS

Run the FastAPI application natively on Windows using a Python virtual environment. IIS continues to serve as the reverse proxy, exactly as planned.

### What Changes

| | Docker Approach | Fallback Approach |
|--|----------------|-------------------|
| App runtime | Linux container | Python directly on Windows |
| Process manager | Docker/gunicorn | NSSM Windows Service |
| ffmpeg | Installed in container | Installed on Windows host |
| Tesseract/Poppler | Installed in container | Installed on Windows host |
| IIS/web.config | Same | Same — no change |
| Application code | Same | Same — no change |

### What the Client Needs to Install (Admin)

1. **Python 3.11** — https://python.org/downloads (check "Add to PATH" during install)
2. **ffmpeg for Windows** — https://ffmpeg.org/download.html → Windows builds → add to PATH
3. **Tesseract for Windows** — https://github.com/UB-Mannheim/tesseract/wiki → set `TESSERACT_CMD` env var
4. **Poppler for Windows** — https://github.com/oschwartz10612/poppler-windows/releases → add `bin/` to PATH
5. **NSSM** (Non-Sucking Service Manager) — https://nssm.cc/download → used to run gunicorn as a Windows Service

### Deployment Steps (No Docker)

```powershell
# 1. Clone repo
cd C:\
git clone <repo-url> cfcchat
cd cfcchat

# 2. Create virtual environment
python -m venv .venv
.\.venv\Scripts\Activate.ps1

# 3. Install CPU-only torch first (avoids 2.5 GB CUDA download)
pip install torch --index-url https://download.pytorch.org/whl/cpu

# 4. Install remaining dependencies
pip install -r requirements.txt

# 5. Set up .env
Copy-Item .env.example .env
notepad .env   # fill in API keys, CORS_ORIGINS=http://192.168.201.211

# 6. Test the app starts
python main.py
# Visit http://127.0.0.1:8000/api/health — should return JSON
# Ctrl+C to stop

# 7. Install as a Windows Service using NSSM
nssm install CFC-ChatAI C:\cfcchat\.venv\Scripts\python.exe
# In NSSM UI:
#   Path: C:\cfcchat\.venv\Scripts\python.exe
#   Arguments: -m gunicorn main:app --workers 2 --worker-class uvicorn.workers.UvicornWorker --bind 127.0.0.1:8000 --timeout 120
#   Startup dir: C:\cfcchat

nssm start CFC-ChatAI

# 8. Deploy frontend to IIS web root
Copy-Item -Path "C:\cfcchat\web\*" -Destination "C:\inetpub\cfcchat" -Recurse -Force
Copy-Item "C:\cfcchat\deployment\iis-web.config" "C:\inetpub\cfcchat\web.config"
```

### Service Management

```powershell
nssm status CFC-ChatAI     # check status
nssm restart CFC-ChatAI    # restart after code changes
nssm stop CFC-ChatAI       # stop
nssm start CFC-ChatAI      # start
```

### Updating After Code Changes

```powershell
cd C:\cfcchat
git pull
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt   # only if requirements changed
nssm restart CFC-ChatAI
Copy-Item -Path ".\web\*" -Destination "C:\inetpub\cfcchat" -Recurse -Force
```

### Disk/RAM Requirements

Same as before except no Docker overhead:
- Python venv + ML packages: ~2 GB disk
- RAM: 4 GB minimum, 8 GB recommended
- No changes to VM_REQUIREMENTS.md specs — still within range

---

## Summary

Docker Desktop is not viable on this VM due to Azure's nested virtualization restrictions. The no-Docker approach runs the same Python application code natively on Windows, uses NSSM to keep it running as a Windows Service, and uses IIS as the reverse proxy exactly as originally planned. No application code changes are required.

# Windows Hosting — Issues Encountered & Caveats

This document captures every issue encountered when deploying CFC Chat AI on the Windows 10 Pro VM, the root cause of each, and how it was resolved. It also outlines the long-term caveats of the client's decision to use Windows over Linux.

---

## Issues Encountered During Deployment

### Issue 1 — Docker Desktop: Virtualization Not Detected
**Symptom:** Docker Desktop showed "Virtualization support not detected" and the engine hung indefinitely at "Starting the Docker Engine..." with no way to quit.

**Root cause:** The VM runs inside Azure's Hyper-V hypervisor. Docker Desktop requires its own nested Hyper-V/WSL2 VM to run Linux containers — a *VM inside a VM*. The Azure VM size provisioned did not support nested virtualization, which is a hardware-level capability that must be explicitly enabled at the Azure infrastructure level.

**Resolution:** Docker Desktop was abandoned entirely. The application was redeployed using Python natively on Windows with NSSM as the process manager. Enabling Hyper-V/VirtualMachinePlatform in Windows Features (Step 2 of the original guide) did not help because the limitation is at the Azure hypervisor level, not the Windows OS level.

**If Ubuntu had been used:** Docker would have run natively without any virtualization requirement. `docker compose up --build` would have succeeded on first try.

---

### Issue 2 — Python 32-bit Installed Instead of 64-bit
**Symptom:** `pip install torch` returned "No matching distribution found." `pip install scikit-learn` tried to compile scipy from source and failed with a missing C compiler error.

**Root cause:** Python was downloaded from the generic python.org download button, which defaults to the 32-bit installer on some Windows configurations. PyTorch and scipy only publish 64-bit Windows wheels — they have no 32-bit builds.

**Resolution:** Uninstalled 32-bit Python, downloaded the explicit `python-3.11.9-amd64.exe` installer, and reinstalled. Verified with `python -c "import platform; print(platform.architecture())"` → `64bit`.

---

### Issue 3 — Windows Python App Execution Alias Intercepts `python` Command
**Symptom:** After installing 64-bit Python, running `python --version` still showed the Microsoft Store redirect message instead of the installed version.

**Root cause:** Windows 10 ships with a built-in "App Execution Alias" for `python` and `python3` that intercepts the command and redirects to the Microsoft Store. This alias takes priority over the real Python in PATH.

**Resolution:** Disabled both aliases in **Settings → Apps → Advanced app settings → App execution aliases**.

---

### Issue 4 — NSSM `$args` is a Reserved PowerShell Variable
**Symptom:** `nssm set CFC-ChatAI AppParameters $args` showed the NSSM help text instead of setting the parameters. The service started but gunicorn launched with no arguments, causing it to immediately pause.

**Root cause:** `$args` is an automatic variable in PowerShell representing the arguments passed to a script or function. Assigning to it is silently ignored; reading from it returns unexpected values.

**Resolution:** Renamed the variable to `$svcArgs` in the deploy script. For the manual fix, arguments were passed directly in the `nssm install` command as a quoted string rather than a variable.

---

### Issue 5 — Gunicorn Does Not Run on Windows
**Symptom:** `gunicorn.exe` crashed immediately with `ModuleNotFoundError: No module named 'fcntl'`.

**Root cause:** Gunicorn is a Linux-only WSGI server. It internally uses `fcntl`, a POSIX system call for file control that does not exist on Windows. This is a fundamental incompatibility — gunicorn has never supported Windows and is not expected to.

**Resolution:** Replaced gunicorn with `uvicorn` running directly. Uvicorn is the underlying ASGI server and runs natively on Windows. The service command changed to:
```
uvicorn.exe main:app --host 127.0.0.1 --port 8000 --workers 1
```

---

### Issue 6 — PyTorch DLL Failed to Load (Missing Visual C++ Runtime)
**Symptom:** The uvicorn worker crashed with `OSError: [WinError 126] The specified module could not be found. Error loading torch\lib\c10.dll`.

**Root cause:** PyTorch's native compiled DLLs depend on the Microsoft Visual C++ Redistributable runtime. This is not installed by default on Windows Server or fresh Windows installs.

**Resolution:** Downloaded and installed `vc_redist.x64.exe` from https://aka.ms/vs/17/release/vc_redist.x64.exe.

---

### Issue 7 — Supabase Service Role Key Not Set
**Symptom:** The app crashed on startup with `RuntimeError: SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required`.

**Root cause:** The `.env` file on the VM was copied from `.env.example` but the `SUPABASE_SERVICE_ROLE_KEY` had not been filled in with the real value. The key is required at module import time.

**Resolution:** Retrieved the service role key from Supabase Dashboard → Project Settings → API → service_role, and added it to `.env` on the VM.

---

### Issue 8 — Frontend Files Served at Wrong Path
**Symptom:** Browser tab showed the app name (from `<title>`) but no UI rendered. All `.jsx` and `.css` files returned 404 in the Network tab, all requested from `/ui/...` paths.

**Root cause:** The deploy script copied frontend files directly to `C:\inetpub\cfcchat\` (the IIS root), but `index.html` references all assets using `/ui/...` absolute paths. IIS looks for those files at `C:\inetpub\cfcchat\ui\...`, which didn't exist.

**Resolution:** Updated the deploy script to copy files into a `ui\` subfolder: `C:\inetpub\cfcchat\ui\`. The app is accessed at `http://192.168.201.211/ui/`.

---

### Issue 9 — Merge Regressions (Repeated)
**Symptom:** After every code merge from the main branch, CORS reverted to `allow_origins=["*"]`, route prefixes lost their `/api/` prefix, and deprecated `on_event` handlers reappeared.

**Root cause:** Conflicting changes in `main.py` between branches were not properly merged.

**Resolution:** Restored CORS, route prefixes, and lifespan context manager each time. Long-term fix: merge `deployment-test` into `main` once the deployment is stable.

---

## Caveats of Choosing Windows Over Linux

### 1. Docker is Not Available (Nested Virtualization)
On this Azure VM configuration, Docker Desktop cannot run. This means:
- The `Dockerfile` and `docker-compose.yml` in the repo are unused
- Deployments require manually managing a Python virtual environment
- ML model updates or dependency changes require running `pip install` on the VM directly

**On Ubuntu:** `docker compose up --build` would handle all of this automatically.

### 2. Higher Ongoing Maintenance Burden
On Linux, a single `deploy.sh` script handles everything. On Windows, the current setup requires:
- Manual NSSM service management
- Manual pip installs if packages change
- More surface area for Windows-specific failures (DLL errors, PATH issues, permissions)

### 3. Process Isolation
Running uvicorn directly (instead of inside Docker) means:
- No isolation between the app and the OS
- A crash in the app can affect the host
- Dependency conflicts between system Python and the venv are possible

### 4. Single Worker Only
Uvicorn on Windows with `--workers > 1` uses Python's `multiprocessing` module with the `spawn` method (Windows doesn't support `fork`). This caused instability in testing. Currently running with `--workers 1`, which limits concurrency.

**On Linux with Docker:** Gunicorn manages multiple uvicorn workers cleanly, providing better throughput for concurrent users.

### 5. No HTTPS Without Additional Setup
Windows does not have Certbot (Let's Encrypt CLI). HTTPS requires either:
- [Win-ACME](https://www.win-acme.com/) — a free IIS-compatible Let's Encrypt client
- A manually purchased SSL certificate imported into IIS
- Azure Application Gateway (paid)

### 6. ffmpeg, Tesseract, Poppler Must Be Managed Manually
On Linux/Docker, these system binaries are installed once in the container image and never drift. On Windows, they are installed globally and any OS update or user action could break them. If a PATH variable is accidentally overwritten, features like video transcription and PDF OCR silently stop working.

---

## Summary

The Windows hosting choice introduced approximately 8 hours of additional setup work compared to the originally planned Ubuntu deployment. Every issue encountered was a Windows-specific compatibility problem:

| Issue | Windows-specific? | Present on Ubuntu? |
|---|---|---|
| Docker nested virtualization | Yes | No |
| 32-bit vs 64-bit Python | Yes | No |
| App Execution Alias | Yes | No |
| NSSM `$args` variable conflict | Yes (PowerShell) | No |
| Gunicorn `fcntl` crash | Yes | No |
| Visual C++ Redistributable | Yes | No |
| Single worker limitation | Yes | No |

The application code itself required zero changes for Windows. All friction was at the infrastructure level.

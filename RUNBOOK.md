# CFC Chat AI — Deployment Runbook

## First-Time Setup (VM, already done — for reference only)

> Prerequisites installed by admin: Python 3.11 (64-bit), Git, ffmpeg, Tesseract, Poppler, NSSM, IIS + URL Rewrite + ARR.
> See `ADMIN_SETUP_GUIDE.md` for full admin setup steps.

```powershell
# 1. Set PowerShell execution policy
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# 2. Clone repo and switch to deployment branch
cd C:\
git clone <repo-url> cfcchat
cd cfcchat
git checkout deployment-test

# If you hit "dubious ownership":
git config --global --add safe.directory C:/cfcchat

# 3. Set up environment variables
Copy-Item .env.example .env
notepad .env
# Set: CORS_ORIGINS=http://192.168.201.211
#      FRONTEND_BASE_URL=http://192.168.201.211
#      All API keys (Supabase, Pinecone, Azure OpenAI)

# 4. Run the deploy script (takes 10-20 min first time — downloads ML packages)
.\deploy-windows.ps1

# 5. Install Windows Service (first time only)
nssm install CFC-ChatAI C:\cfcchat\.venv\Scripts\uvicorn.exe "main:app --host 127.0.0.1 --port 8000 --workers 1 --log-level info"
nssm set CFC-ChatAI AppDirectory C:\cfcchat
nssm set CFC-ChatAI AppStdout C:\cfcchat\logs\stdout.log
nssm set CFC-ChatAI AppStderr C:\cfcchat\logs\stderr.log
nssm set CFC-ChatAI AppStdoutCreationDisposition 4
nssm set CFC-ChatAI AppStderrCreationDisposition 4
New-Item -ItemType Directory -Force C:\cfcchat\logs
nssm start CFC-ChatAI

# 6. Set up IIS site
# IIS Manager → Default Web Site → Advanced Settings → Physical Path = C:\inetpub\cfcchat

# 7. Verify everything is working
nssm status CFC-ChatAI                              # should say SERVICE_RUNNING
Invoke-RestMethod http://127.0.0.1:8000/api/health  # should return JSON
# Open browser: http://127.0.0.1/ui/               # should show login page
```

---

## Updating the App (after pushing code changes to GitHub)

Run this on the VM every time you push new code:

```powershell
cd C:\cfcchat

# 1. Pull latest code from the deployment branch
git pull origin <branch-name>

# 2. Re-run deploy script
#    - Updates Python packages if requirements.txt changed
#    - Copies new frontend files to IIS root
#    - Restarts the Windows Service automatically
.\deploy-windows.ps1

# 3. Verify
nssm status CFC-ChatAI
Invoke-RestMethod http://127.0.0.1:8000/api/health
```

> If the service doesn't restart automatically, run: `nssm restart CFC-ChatAI`

---

## Checking Logs

```powershell
# View live application output
Get-Content C:\cfcchat\logs\stdout.log -Tail 50
Get-Content C:\cfcchat\logs\stderr.log -Tail 50

# Check service status
nssm status CFC-ChatAI

# Restart service manually
nssm restart CFC-ChatAI
nssm stop CFC-ChatAI
nssm start CFC-ChatAI
```

---

## When a Domain Name is Acquired

When a domain (e.g. `chat.cfctech.com`) is registered and pointed to the VM's public IP, make these changes:

### 1. Update `.env` on the VM
```
CORS_ORIGINS=https://chat.cfctech.com
FRONTEND_BASE_URL=https://chat.cfctech.com
```
Then: `nssm restart CFC-ChatAI`

### 2. Update Supabase Auth Settings
- Supabase Dashboard → **Authentication → URL Configuration**
- **Site URL**: `https://chat.cfctech.com`
- **Redirect URLs**: add `https://chat.cfctech.com/*`

### 3. Set Up HTTPS in IIS
Use [Win-ACME](https://www.win-acme.com/) (free Let's Encrypt client for IIS):
```powershell
# Download win-acme, run as admin:
.\wacs.exe --source manual --host chat.cfctech.com --installation iis --siteid 1
```
This auto-installs and renews the SSL certificate in IIS.

### 4. Update IIS Site Binding
- IIS Manager → Default Web Site → **Bindings** → Add:
  - Type: `https`, Port: `443`, Host name: `chat.cfctech.com`, Certificate: *(select the Let's Encrypt cert)*
- Keep the port 80 binding and add an HTTP→HTTPS redirect rule

### 5. Update `iis-web.config` (optional hardening)
Add an HTTPS redirect rule so `http://` automatically redirects to `https://`.

---

## Current Access URLs

| What | URL |
|------|-----|
| App (UI) | http://192.168.201.211/ui/ |
| API health check | http://192.168.201.211/api/health |
| API docs (dev only) | http://192.168.201.211/docs |
| VM RDP | 192.168.201.211:3389 (via SonicWall VPN) |

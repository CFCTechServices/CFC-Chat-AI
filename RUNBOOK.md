# CFC Chat AI — Operations Runbook

Quick reference for day-to-day operations on the Windows VM.
For full setup details see `DEPLOYMENT.md`. For admin prerequisites see `ADMIN_SETUP_GUIDE.md`.

---

## Deploy an Update

```powershell
cd C:\cfcchat
git pull
.\deploy-windows.ps1
```

The script updates packages, deploys frontend files, and restarts the service automatically.

---

## Restart the Service

```powershell
nssm stop CFC-ChatAI
Start-Sleep -Seconds 3

# Kill any stale Python processes still holding port 8000
$stale = netstat -ano | Select-String ":8000\s.*LISTENING" | ForEach-Object { ($_ -split '\s+')[-1] }
foreach ($pid in $stale) { Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue }

nssm start CFC-ChatAI
```

---

## View Logs

```powershell
Get-Content C:\cfcchat\logs\stderr.log -Tail 50    # errors / warnings
Get-Content C:\cfcchat\logs\stdout.log -Tail 50    # general output
```

---

## Health Check

```powershell
Invoke-RestMethod http://127.0.0.1:8000/api/health
```

---

## Check Running Process

```powershell
# StartTime should match your last deploy — if it's old, the old code is still running
Get-Process -Name python | Select-Object Id, StartTime
netstat -ano | findstr ":8000"
```

---

## Update an Env Variable

1. Edit `C:\cfcchat\.env`
2. Run `.\deploy-windows.ps1` (required — the process holds vars in memory)

---

## Azure OpenAI Model Expired

If logs show `410 ModelDeprecated`:

1. Azure OpenAI Studio → Deployments → create new deployment (e.g. `gpt-4o-mini`)
2. Update `AZURE_OPENAI_DEPLOYMENT=<new-name>` in `.env`
3. Run `.\deploy-windows.ps1`

---

## When a Domain Name is Acquired

1. Update `.env`: `CORS_ORIGINS=https://your-domain.com` and `FRONTEND_BASE_URL=https://your-domain.com`
2. Run `.\deploy-windows.ps1`
3. Supabase Dashboard → Authentication → URL Configuration → update Site URL and Redirect URLs
4. Install SSL via [Win-ACME](https://www.win-acme.com/) (free Let's Encrypt for IIS)
5. Update `TEAM_ACCESS_GUIDE.md` with the new public URL

---

## Current Access URLs

| What | URL |
|------|-----|
| App (UI) | http://192.168.201.211/ui/ |
| API health check | http://192.168.201.211/api/health |
| VM RDP | 192.168.201.211:3389 (via SonicWall VPN) |

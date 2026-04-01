# No-Docker Windows Deployment Script
# deploy-windows.ps1 — Deploy CFC Chat AI natively on Windows (no Docker)
#
# Prerequisites (must be installed by admin first - see ADMIN_SETUP_GUIDE.md):
#   - Python 3.11 on PATH
#   - ffmpeg on PATH
#   - Tesseract on PATH
#   - Poppler on PATH
#   - NSSM in System32
#   - IIS with URL Rewrite and ARR enabled
#
# Usage (run from C:\cfcchat):
#   .\deploy-windows.ps1           # first deployment
#   .\deploy-windows.ps1 -Update   # subsequent updates

param(
    [switch]$Update = $false,
    [string]$WebRoot = "C:\inetpub\cfcchat",
    [string]$ServiceName = "CFC-ChatAI"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "============================================================"
Write-Host " CFC Chat AI - Deploy (Windows, No Docker)"
Write-Host "  Project : $ScriptDir"
Write-Host "  Web root: $WebRoot"
Write-Host "============================================================"

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[0/4] Running pre-flight checks..."

foreach ($cmd in @("python", "pip", "ffmpeg", "nssm")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: '$cmd' not found on PATH. See ADMIN_SETUP_GUIDE.md." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path "$ScriptDir\.env")) {
    Write-Host "ERROR: .env file not found. Copy .env.example to .env and fill in values." -ForegroundColor Red
    exit 1
}
Write-Host "      All checks passed."

# ---------------------------------------------------------------------------
# Step 1: Create/update Python virtual environment
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[1/4] Setting up Python virtual environment..."

if (-not (Test-Path "$ScriptDir\.venv")) {
    python -m venv "$ScriptDir\.venv"
    Write-Host "      Virtual environment created."
} else {
    Write-Host "      Virtual environment already exists."
}

$pip = "$ScriptDir\.venv\Scripts\pip.exe"
$python = "$ScriptDir\.venv\Scripts\python.exe"

# Install CPU-only torch first to avoid 2.5 GB CUDA download
Write-Host "      Installing CPU-only torch (this may take a few minutes on first run)..."
& $pip install --quiet torch --index-url https://download.pytorch.org/whl/cpu

# Install remaining requirements
Write-Host "      Installing application dependencies..."
& $pip install --quiet -r "$ScriptDir\requirements.txt"
Write-Host "      Done - Python environment ready."

# ---------------------------------------------------------------------------
# Step 2: Deploy static frontend files to IIS web root
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/4] Copying frontend static files to $WebRoot\ui\..."
if (-not (Test-Path $WebRoot)) {
    New-Item -ItemType Directory -Force -Path $WebRoot | Out-Null
}
$uiRoot = "$WebRoot\ui"
if (-not (Test-Path $uiRoot)) {
    New-Item -ItemType Directory -Force -Path $uiRoot | Out-Null
}

# Copy web files into the ui/ subfolder (index.html references /ui/* paths)
Copy-Item -Path "$ScriptDir\web\*" -Destination $uiRoot -Recurse -Force

# Copy IIS web.config to the site root (one level above ui/)
Copy-Item -Path "$ScriptDir\deployment\iis-web.config" -Destination "$WebRoot\web.config" -Force
Write-Host "      Done - frontend deployed to $uiRoot"

# ---------------------------------------------------------------------------
# Step 3: Install or restart Windows Service
# ---------------------------------------------------------------------------
Write-Host ""
$uvicorn = "$ScriptDir\.venv\Scripts\uvicorn.exe"
$svcArgs = "main:app --host 127.0.0.1 --port 8000 --workers 2 --log-level info"

$serviceExists = $false
try {
    $serviceStatus = (nssm status $ServiceName 2>&1) -join ""
    if ($serviceStatus -match "SERVICE_RUNNING|SERVICE_STOPPED|SERVICE_PAUSED") {
        $serviceExists = $true
    }
} catch {
    $serviceExists = $false
}

if ($serviceExists) {
    Write-Host "[3/4] Restarting existing Windows Service '$ServiceName'..."
    nssm restart $ServiceName
} else {
    Write-Host "[3/4] Installing Windows Service '$ServiceName'..."
    nssm install $ServiceName $uvicorn $svcArgs
    nssm set $ServiceName AppDirectory $ScriptDir
    # Pass the .env file path so the service can load environment variables
    nssm set $ServiceName AppEnvironmentExtra "DOTENV_PATH=$ScriptDir\.env"
    nssm start $ServiceName
}
Write-Host "      Done - service running."

# ---------------------------------------------------------------------------
# Step 4: Verify
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/4] Verifying backend health..."
Start-Sleep -Seconds 5
try {
    $health = Invoke-RestMethod "http://127.0.0.1:8000/api/health" -ErrorAction Stop
    Write-Host "      Health check: OK" -ForegroundColor Green
} catch {
    Write-Host "      WARNING: Health check failed. Check logs: nssm status $ServiceName" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================"
Write-Host " Deployment complete!"
Write-Host ""
Write-Host "  Backend API (internal): http://127.0.0.1:8000"
Write-Host "  Health check:           Invoke-RestMethod http://127.0.0.1:8000/api/health"
Write-Host "  Service logs:           nssm status $ServiceName"
Write-Host "  Event logs:             Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Host ""
Write-Host "  Frontend files:         $WebRoot"
Write-Host "  IIS must be running and configured - see deployment\iis-web.config"
Write-Host "============================================================"

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
    [string]$ServiceName = "CFC-ChatAI",
    [string]$DotNetServiceName = "CFC-ChatAI-DotNet",
    [string]$DotNetPort = "5000",
    [string]$DotNetPublishDir = ""
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
Write-Host "[0/5] Running pre-flight checks..."

foreach ($cmd in @("python", "pip", "ffmpeg", "nssm", "dotnet")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        if ($cmd -eq "dotnet") {
            Write-Host "ERROR: 'dotnet' not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download" -ForegroundColor Red
        } else {
            Write-Host "ERROR: '$cmd' not found on PATH. See ADMIN_SETUP_GUIDE.md." -ForegroundColor Red
        }
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
Write-Host "[1/5] Setting up Python virtual environment..."

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
Write-Host "[2/5] Copying frontend static files to $WebRoot\ui\..."
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
# Step 3: Install or restart Python Windows Service
# ---------------------------------------------------------------------------
Write-Host "[3/5] Installing/restarting Python backend service..."
$uvicorn = "$ScriptDir\.venv\Scripts\uvicorn.exe"
$svcArgs = "main:app --host 127.0.0.1 --port 8000 --workers 2 --log-level info"

$serviceExists = $null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)

if ($serviceExists) {
    Write-Host "[3/5] Restarting existing Windows Service '$ServiceName'..."
    # Stop the service, then force-kill any lingering Python processes that
    # survived (nssm restart alone can leave stale workers holding port 8000).
    nssm stop $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 3
    $stale = netstat -ano | Select-String ":8000\s.*LISTENING" | ForEach-Object {
        ($_ -split '\s+')[-1]
    } | Select-Object -Unique
    foreach ($pid in $stale) {
        if ($pid -match '^\d+$' -and $pid -ne '0') {
            Write-Host "      Killing stale PID $pid on :8000..."
            Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 2
    nssm start $ServiceName
} else {
    Write-Host "[3/5] Installing Windows Service '$ServiceName'..."
    nssm install $ServiceName $uvicorn $svcArgs
    nssm set $ServiceName AppDirectory $ScriptDir
    # Pass the .env file path so the service can load environment variables
    nssm set $ServiceName AppEnvironmentExtra "DOTENV_PATH=$ScriptDir\.env"
    nssm start $ServiceName
}
Write-Host "      Done - Python service running."

# ---------------------------------------------------------------------------
# Step 4: Publish and install/restart .NET (C#) API service
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/5] Publishing .NET API..."
if ([string]::IsNullOrWhiteSpace($DotNetPublishDir)) {
    $DotNetPublishDir = "$ScriptDir\dotnet-publish"
}
$dotNetProject = "$ScriptDir\dotnet\src\CFC.ChatAI.Api\CFC.ChatAI.Api.csproj"
if (-not (Test-Path $dotNetProject)) {
    Write-Host "ERROR: .NET API project not found at $dotNetProject" -ForegroundColor Red
    exit 1
}
& dotnet publish $dotNetProject -c Release -o $DotNetPublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: 'dotnet publish' failed." -ForegroundColor Red
    exit 1
}
Write-Host "      Done - published to $DotNetPublishDir"

Write-Host ""
Write-Host "[4/5] Installing/restarting .NET API service '$DotNetServiceName' (:$DotNetPort)..."
$dotNetExe = "$DotNetPublishDir\CFC.ChatAI.Api.exe"
if (-not (Test-Path $dotNetExe)) {
    Write-Host "ERROR: Expected $dotNetExe after publish." -ForegroundColor Red
    exit 1
}
$dotNetEnv = "ASPNETCORE_URLS=http://127.0.0.1:$DotNetPort", "DOTNET_ENVIRONMENT=Production"
$dotNetLogDir = "$DotNetPublishDir\logs"
if (-not (Test-Path $dotNetLogDir)) {
    New-Item -ItemType Directory -Force -Path $dotNetLogDir | Out-Null
}

$dotNetExists = $null -ne (Get-Service -Name $DotNetServiceName -ErrorAction SilentlyContinue)

if ($dotNetExists) {
    Write-Host "      Restarting existing Windows Service '$DotNetServiceName'..."
    nssm stop $DotNetServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 3
    $staleDotNet = netstat -ano | Select-String ":$DotNetPort\s.*LISTENING" | ForEach-Object {
        ($_ -split '\s+')[-1]
    } | Select-Object -Unique
    foreach ($pid in $staleDotNet) {
        if ($pid -match '^\d+$' -and $pid -ne '0') {
            Write-Host "      Killing stale PID $pid on :$DotNetPort..."
            Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 2
    nssm set $DotNetServiceName Application $dotNetExe
    nssm set $DotNetServiceName AppDirectory $DotNetPublishDir
    nssm set $DotNetServiceName AppEnvironmentExtra ($dotNetEnv -join ' ')
    nssm set $DotNetServiceName AppStdout "$dotNetLogDir\stdout.log"
    nssm set $DotNetServiceName AppStderr "$dotNetLogDir\stderr.log"
    nssm set $DotNetServiceName AppRotateFiles 1
    nssm set $DotNetServiceName AppRotateOnline 1
    nssm start $DotNetServiceName
} else {
    Write-Host "      Installing Windows Service '$DotNetServiceName'..."
    nssm install $DotNetServiceName $dotNetExe
    nssm set $DotNetServiceName AppDirectory $DotNetPublishDir
    nssm set $DotNetServiceName AppEnvironmentExtra ($dotNetEnv -join ' ')
    nssm set $DotNetServiceName AppStdout "$dotNetLogDir\stdout.log"
    nssm set $DotNetServiceName AppStderr "$dotNetLogDir\stderr.log"
    nssm set $DotNetServiceName AppRotateFiles 1
    nssm set $DotNetServiceName AppRotateOnline 1
    nssm start $DotNetServiceName
}
Write-Host "      Done - .NET service running."

# ---------------------------------------------------------------------------
# Step 5: Verify
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[5/5] Verifying backend health (polling up to ~90s, services cold-start)..."
function Wait-Health($Url, $Label, $Svc) {
    for ($i = 1; $i -le 18; $i++) {
        try {
            $r = Invoke-RestMethod $Url -TimeoutSec 5 -ErrorAction Stop
            Write-Host "      ${Label}: OK (attempt $i)" -ForegroundColor Green
            return $r
        } catch {
            Write-Host "      ${Label}: waiting... (attempt $i/18)"
            Start-Sleep -Seconds 5
        }
    }
    Write-Host "      WARNING: ${Label} failed. Check logs: nssm status $Svc" -ForegroundColor Yellow
    return $null
}
Wait-Health "http://127.0.0.1:8000/api/health" "Python health check (:8000)" $ServiceName | Out-Null
Wait-Health "http://127.0.0.1:$DotNetPort/api/health" ".NET health check (:$DotNetPort)" $DotNetServiceName | Out-Null

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================"
Write-Host " Deployment complete!"
Write-Host ""
Write-Host "  Backend API - Python (internal): http://127.0.0.1:8000"
Write-Host "  Backend API - .NET (internal):   http://127.0.0.1:$DotNetPort"
Write-Host "  Health checks:                  Invoke-RestMethod http://127.0.0.1:8000/api/health"
Write-Host "                                  Invoke-RestMethod http://127.0.0.1:$DotNetPort/api/health"
Write-Host "  Service logs:                   nssm status $ServiceName"
Write-Host "                                  nssm status $DotNetServiceName"
Write-Host "  Event logs:             Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Host ""
Write-Host "  Frontend files:         $WebRoot"
Write-Host "  IIS must be running and configured - see deployment\iis-web.config"
Write-Host "============================================================"

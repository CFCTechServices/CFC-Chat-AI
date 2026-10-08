# No-Docker Windows Deployment Script
# deploy-windows.ps1 — Deploy CFC Chat AI natively on Windows (no Docker)
#
# Prerequisites (must be installed by admin first - see ADMIN_SETUP_GUIDE.md):
#   - Python 3.11 on PATH
#   - .NET 10 SDK on PATH
#   - ffmpeg on PATH
#   - Tesseract on PATH
#   - Poppler on PATH
#   - NSSM in System32
#   - IIS with URL Rewrite and ARR enabled
#
# Usage (run from repository root as Administrator):
#   .\deploy-windows.ps1                          # Full deployment
#   .\deploy-windows.ps1 -Update                  # Fast update (skips redundant dependency checks)
#   .\deploy-windows.ps1 -SkipPython              # Deploy only Frontend and .NET API
#   .\deploy-windows.ps1 -SkipDotNet              # Deploy only Frontend and Python API
#   .\deploy-windows.ps1 -SkipFrontend            # Deploy backend services only

param(
    [switch]$Update = $false,
    [switch]$SkipPython = $false,
    [switch]$SkipDotNet = $false,
    [switch]$SkipFrontend = $false,
    [switch]$SkipDeps = $false,
    [string]$WebRoot = "C:\inetpub\cfcchat",
    [string]$ServiceName = "CFC-ChatAI",
    [string]$DotNetServiceName = "CFC-ChatAI-DotNet",
    [string]$PythonPort = "8000",
    [string]$DotNetPort = "5000",
    [string]$DotNetPublishDir = "$PSScriptRoot\dotnet-publish"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$LogDir = "$ScriptDir\logs"

# -Update implies skipping redundant dependency checks if virtualenv already exists
if ($Update) {
    $SkipDeps = $true
}

# ---------------------------------------------------------------------------
# Reusable Helper Functions
# ---------------------------------------------------------------------------

function Stop-PortProcess([int]$Port) {
    # Free TCP port by stopping any lingering process holding it
    $targetPids = @()
    try {
        if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
            $targetPids = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty OwningProcess -Unique)
        }
    } catch {
        # Fall back to netstat
    }

    if (-not $targetPids -or $targetPids.Count -eq 0) {
        $targetPids = @(netstat -ano | Select-String ":$Port\s.*LISTENING" | ForEach-Object {
            ($_ -split '\s+')[-1]
        } | Select-Object -Unique)
    }

    foreach ($procId in $targetPids) {
        if ($procId -match '^\d+$' -and [int]$procId -gt 0 -and [int]$procId -ne $PID) {
            Write-Host "      Stopping process $procId holding port $Port..."
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Stop-NssmService([string]$Name, [int]$Port) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
        if ($svc.Status -ne 'Stopped') {
            Write-Host "      Stopping service '$Name'..."
            & {
                $ErrorActionPreference = 'SilentlyContinue'
                & nssm stop $Name 2>&1 | Out-Null
            }
            Start-Sleep -Seconds 2
        }
    }
    Stop-PortProcess -Port $Port

    # If stopping the .NET service, ensure any orphaned CFC.ChatAI.Api process is terminated
    if ($Name -eq $DotNetServiceName) {
        Get-Process -Name "CFC.ChatAI.Api" -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Deploy-NssmService {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$AppPath,
        [string]$Arguments = "",
        [Parameter(Mandatory = $true)][string]$AppDir,
        [Parameter(Mandatory = $true)][int]$Port,
        [string]$EnvironmentExtra = "",
        [string]$LogPrefix = "app"
    )

    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue

    if ($null -eq $svc) {
        Write-Host "      Installing new service '$Name'..."
        & {
            $ErrorActionPreference = 'SilentlyContinue'
            & nssm install $Name $AppPath 2>&1 | Out-Null
        }
    } else {
        # Ensure stopped and port is free before configuring
        Stop-NssmService -Name $Name -Port $Port
    }

    # Ensure log directory exists
    if (-not (Test-Path $LogDir)) {
        New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    }

    # Apply configuration (idempotent; suppress verbose 'Set parameter...' messages)
    & {
        $ErrorActionPreference = 'SilentlyContinue'
        & nssm set $Name Application $AppPath 2>&1 | Out-Null
        if ($Arguments) {
            & nssm set $Name AppParameters $Arguments 2>&1 | Out-Null
        }
        & nssm set $Name AppDirectory $AppDir 2>&1 | Out-Null
        if ($EnvironmentExtra) {
            & nssm set $Name AppEnvironmentExtra $EnvironmentExtra 2>&1 | Out-Null
        }
        & nssm set $Name AppStdout "$LogDir\${LogPrefix}_stdout.log" 2>&1 | Out-Null
        & nssm set $Name AppStderr "$LogDir\${LogPrefix}_stderr.log" 2>&1 | Out-Null
        & nssm set $Name AppRotateFiles 1 2>&1 | Out-Null
        & nssm set $Name AppRotateOnline 1 2>&1 | Out-Null
    }

    Write-Host "      Starting service '$Name'..."
    & {
        $ErrorActionPreference = 'SilentlyContinue'
        & nssm start $Name 2>&1 | Out-Null
    }
    Start-Sleep -Seconds 2
}

function Test-EndpointHealth([string]$Url, [string]$Label, [string]$Svc, [int]$MaxAttempts = 18) {
    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            $r = Invoke-RestMethod $Url -TimeoutSec 5 -ErrorAction Stop
            Write-Host "      ${Label}: OK (attempt $i)" -ForegroundColor Green
            return $true
        } catch {
            Write-Host "      ${Label}: waiting... (attempt $i/$MaxAttempts)"
            Start-Sleep -Seconds 5
        }
    }
    Write-Host "      ERROR: ${Label} failed after $MaxAttempts attempts. Check logs: nssm status $Svc" -ForegroundColor Red
    return $false
}

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------
Write-Host "============================================================"
Write-Host " CFC Chat AI - Deploy (Windows, No Docker)"
Write-Host "  Project : $ScriptDir"
Write-Host "  Web root: $WebRoot"
Write-Host "  Logs    : $LogDir"
Write-Host "============================================================"
Write-Host ""
Write-Host "[0/5] Running pre-flight checks..."

$requiredTools = @("python", "ffmpeg", "nssm", "dotnet")
foreach ($cmd in $requiredTools) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        if ($cmd -eq "dotnet") {
            Write-Host "ERROR: 'dotnet' not found on PATH. Install .NET SDK from https://dotnet.microsoft.com/download" -ForegroundColor Red
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
# Step 1: Python Virtual Environment & Dependencies
# ---------------------------------------------------------------------------
if (-not $SkipPython) {
    Write-Host ""
    Write-Host "[1/5] Setting up Python virtual environment..."

    if (-not (Test-Path "$ScriptDir\.venv")) {
        python -m venv "$ScriptDir\.venv"
        Write-Host "      Virtual environment created."
    } else {
        Write-Host "      Virtual environment exists."
    }

    $pip = "$ScriptDir\.venv\Scripts\pip.exe"
    $python = "$ScriptDir\.venv\Scripts\python.exe"

    if (-not $SkipDeps) {
        # Check if PyTorch is already installed to avoid slow remote PyPI index queries
        $torchVersion = & $python -c "import torch; print(torch.__version__)" 2>$null
        if (-not $torchVersion) {
            Write-Host "      Installing CPU-only PyTorch (first run, ~200MB)..."
            & $pip install --quiet torch --index-url https://download.pytorch.org/whl/cpu
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Failed to install PyTorch." -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "      PyTorch already installed ($torchVersion)."
        }

        Write-Host "      Installing application dependencies..."
        & $pip install --quiet -r "$ScriptDir\requirements.txt"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to install requirements.txt dependencies." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "      Skipping dependency checks (-SkipDeps / -Update)."
    }
    Write-Host "      Done - Python environment ready."
} else {
    Write-Host ""
    Write-Host "[1/5] Skipping Python environment setup (-SkipPython)."
}

# ---------------------------------------------------------------------------
# Step 2: Deploy Frontend static files and IIS web.config
# ---------------------------------------------------------------------------
if (-not $SkipFrontend) {
    Write-Host ""
    $uiRoot = "$WebRoot\ui"
    Write-Host "[2/5] Copying frontend static files to $uiRoot..."

    if (-not (Test-Path $uiRoot)) {
        New-Item -ItemType Directory -Force -Path $uiRoot | Out-Null
    }

    Copy-Item -Path "$ScriptDir\web\*" -Destination $uiRoot -Recurse -Force

    $iisConfig = "$ScriptDir\deployment\iis-web.config"
    if (Test-Path $iisConfig) {
        Copy-Item -Path $iisConfig -Destination "$WebRoot\web.config" -Force
    } else {
        Write-Host "WARNING: $iisConfig not found; skipping web.config copy." -ForegroundColor Yellow
    }
    Write-Host "      Done - frontend deployed to $uiRoot"
} else {
    Write-Host ""
    Write-Host "[2/5] Skipping frontend deployment (-SkipFrontend)."
}

# ---------------------------------------------------------------------------
# Step 3: Python Windows Service (uvicorn FastAPI)
# ---------------------------------------------------------------------------
if (-not $SkipPython) {
    Write-Host ""
    Write-Host "[3/5] Configuring Python backend service '$ServiceName' (:$PythonPort)..."

    $uvicorn = "$ScriptDir\.venv\Scripts\uvicorn.exe"
    $svcArgs = "main:app --host 127.0.0.1 --port $PythonPort --workers 2 --log-level info"
    $pyEnv   = "DOTENV_PATH=$ScriptDir\.env"

    Deploy-NssmService `
        -Name $ServiceName `
        -AppPath $uvicorn `
        -Arguments $svcArgs `
        -AppDir $ScriptDir `
        -Port ([int]$PythonPort) `
        -EnvironmentExtra $pyEnv `
        -LogPrefix "python"

    Write-Host "      Done - Python service running."
} else {
    Write-Host ""
    Write-Host "[3/5] Skipping Python service restart (-SkipPython)."
}

# ---------------------------------------------------------------------------
# Step 4: .NET (C#) API Publish & Windows Service
# ---------------------------------------------------------------------------
if (-not $SkipDotNet) {
    Write-Host ""
    Write-Host "[4/5] Publishing and configuring .NET API service '$DotNetServiceName' (:$DotNetPort)..."

    # Stop the running .NET service and release port before publish to avoid file locking on binaries
    Stop-NssmService -Name $DotNetServiceName -Port ([int]$DotNetPort)

    $dotNetProject = "$ScriptDir\dotnet\src\CFC.ChatAI.Api\CFC.ChatAI.Api.csproj"
    if (-not (Test-Path $dotNetProject)) {
        Write-Host "ERROR: .NET project not found at $dotNetProject" -ForegroundColor Red
        exit 1
    }

    & dotnet publish $dotNetProject -c Release -o $DotNetPublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: 'dotnet publish' failed." -ForegroundColor Red
        exit 1
    }

    $dotNetExe = "$DotNetPublishDir\CFC.ChatAI.Api.exe"
    if (-not (Test-Path $dotNetExe)) {
        Write-Host "ERROR: Expected executable $dotNetExe not found after publish." -ForegroundColor Red
        exit 1
    }

    Deploy-NssmService `
        -Name $DotNetServiceName `
        -AppPath $dotNetExe `
        -Arguments "--urls http://127.0.0.1:$DotNetPort" `
        -AppDir $DotNetPublishDir `
        -Port ([int]$DotNetPort) `
        -EnvironmentExtra "DOTNET_ENVIRONMENT=Production`nDOTENV_PATH=$ScriptDir\.env" `
        -LogPrefix "dotnet"

    Write-Host "      Done - .NET service running."
} else {
    Write-Host ""
    Write-Host "[4/5] Skipping .NET API build & service restart (-SkipDotNet)."
}

# ---------------------------------------------------------------------------
# Step 5: Verification & Health Checks
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[5/5] Verifying backend health (polling up to ~90s cold-start)..."

$healthPassed = $true

if (-not $SkipPython) {
    $pyHealth = Test-EndpointHealth `
        -Url "http://127.0.0.1:$PythonPort/api/health" `
        -Label "Python health check (:$PythonPort)" `
        -Svc $ServiceName
    if (-not $pyHealth) { $healthPassed = $false }
}

if (-not $SkipDotNet) {
    $dotnetHealth = Test-EndpointHealth `
        -Url "http://127.0.0.1:$DotNetPort/api/health" `
        -Label ".NET health check (:$DotNetPort)" `
        -Svc $DotNetServiceName
    if (-not $dotnetHealth) { $healthPassed = $false }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================"
if ($healthPassed) {
    Write-Host " Deployment complete! All running services healthy." -ForegroundColor Green
} else {
    Write-Host " DEPLOYMENT FAILED: One or more health checks did not succeed." -ForegroundColor Red
}
Write-Host ""
if (-not $SkipPython) {
    Write-Host "  Python API:     http://127.0.0.1:$PythonPort"
    Write-Host "  Python Logs:    $LogDir\python_stdout.log / python_stderr.log"
}
if (-not $SkipDotNet) {
    Write-Host "  .NET API:       http://127.0.0.1:$DotNetPort"
    Write-Host "  .NET Logs:      $LogDir\dotnet_stdout.log / dotnet_stderr.log"
}
Write-Host "  Frontend files: $WebRoot\ui"
Write-Host "  IIS site:       Configured via $WebRoot\web.config"
Write-Host "============================================================"

if (-not $healthPassed) {
    exit 1
}
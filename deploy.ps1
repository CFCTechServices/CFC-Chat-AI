# deploy.ps1 — Build and deploy CFC Chat AI on a Windows VM
#
# What this script does:
#   1. Copies the static frontend (web/) to the IIS web root
#   2. Rebuilds the backend Docker image
#   3. Restarts the backend container via Docker Compose
#
# Prerequisites:
#   - Docker Desktop installed and running (WSL2 backend)
#   - IIS installed with URL Rewrite and ARR modules
#   - A .env file with production values in the project root
#
# Usage (run as Administrator):
#   .\deploy.ps1                               # uses default web root
#   .\deploy.ps1 -WebRoot "D:\www\cfcchat"     # custom web root

param(
    [string]$WebRoot = "C:\inetpub\cfcchat"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "============================================================"
Write-Host " CFC Chat AI - Deploy (Windows)"
Write-Host "  Project : $ScriptDir"
Write-Host "  Web root: $WebRoot"
Write-Host "============================================================"

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[0/3] Running pre-flight checks..."

# Check Docker is running
try {
    docker info | Out-Null
} catch {
    Write-Host "ERROR: Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}
Write-Host "      Docker is running."

# Check .env exists
if (-not (Test-Path "$ScriptDir\.env")) {
    Write-Host "ERROR: .env file not found in $ScriptDir" -ForegroundColor Red
    Write-Host "       Copy .env.example to .env and fill in production values."
    exit 1
}
Write-Host "      .env file found."

# ---------------------------------------------------------------------------
# Step 1: Deploy static frontend files
#
# The frontend is vanilla JSX files — no build step required.
# We copy web/ to the IIS web root so IIS can serve them directly.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[1/3] Copying frontend static files to $WebRoot..."

if (-not (Test-Path $WebRoot)) {
    New-Item -ItemType Directory -Force -Path $WebRoot | Out-Null
}

# Copy all files from web/ to the IIS root, preserving structure
Copy-Item -Path "$ScriptDir\web\*" -Destination $WebRoot -Recurse -Force
Write-Host "      Done - frontend files deployed to $WebRoot"

# ---------------------------------------------------------------------------
# Step 2: Rebuild the Docker image
#
# --no-cache ensures a fresh install of Python packages so that any
# requirements.txt changes are picked up even if layers were cached.
# Remove --no-cache if you want faster rebuilds that reuse cached layers.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/3] Rebuilding backend Docker image..."
Push-Location $ScriptDir
try {
    docker compose build --no-cache
    Write-Host "      Done - image built."
} finally {
    Pop-Location
}

# ---------------------------------------------------------------------------
# Step 3: Restart the backend container
#
# 'up -d' starts new containers and replaces any running ones.
# The data/ directory is mounted as a volume so no data is lost on restart.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[3/3] Restarting backend service..."
Push-Location $ScriptDir
try {
    docker compose up -d
    Write-Host "      Done - backend restarted."
} finally {
    Pop-Location
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
Write-Host "  Container logs:         docker compose logs -f backend"
Write-Host ""
Write-Host "  Frontend files:         $WebRoot"
Write-Host "  IIS must be running and configured - see deployment\iis-web.config"
Write-Host "============================================================"

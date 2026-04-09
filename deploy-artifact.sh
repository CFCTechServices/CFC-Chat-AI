#!/usr/bin/env bash
# deploy.sh — Build and deploy CFC Chat AI to the Azure VM
#
# What this script does:
#   1. Copies the static frontend (web/) to the nginx web root
#   2. Rebuilds the backend Docker image
#   3. Restarts the backend container via Docker Compose
#
# Prerequisites:
#   - Docker and Docker Compose v2 installed
#   - Nginx installed and configured on the host (see deployment/nginx.conf.example)
#   - A .env file with production values exists in the project root
#
# Usage:
#   ./deploy.sh                      # uses default web root /var/www/cfcchat
#   ./deploy.sh /var/www/custom-dir  # override web root

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
WEB_ROOT="${1:-/var/www/cfcchat}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "============================================================"
echo " CFC Chat AI — Deploy"
echo "  Project : $SCRIPT_DIR"
echo "  Web root: $WEB_ROOT"
echo "============================================================"

# ---------------------------------------------------------------------------
# Step 1: Deploy static frontend files
#
# The frontend is vanilla JSX files — no build step required.
# We copy web/ to the nginx web root so nginx can serve them directly.
# The nginx alias directive maps /ui/ requests to this directory.
# ---------------------------------------------------------------------------
echo ""
echo "[1/3] Copying frontend static files to $WEB_ROOT..."
sudo mkdir -p "$WEB_ROOT"
sudo cp -r "$SCRIPT_DIR/web/." "$WEB_ROOT/"
echo "      Done — frontend files deployed to $WEB_ROOT"

# ---------------------------------------------------------------------------
# Step 2: Rebuild the Docker image
#
# --no-cache ensures a fresh install of Python packages so that any
# requirements.txt changes are picked up even if layers were cached.
# Remove --no-cache if you want faster rebuilds that reuse cached layers.
# ---------------------------------------------------------------------------
echo ""
echo "[2/3] Rebuilding backend Docker image..."
docker compose -f "$SCRIPT_DIR/docker-compose.yml" build --no-cache
echo "      Done — image built."

# ---------------------------------------------------------------------------
# Step 3: Restart the backend container
#
# `up -d` starts new containers and replaces any running ones.
# The data/ directory is mounted as a volume so no data is lost on restart.
# ---------------------------------------------------------------------------
echo ""
echo "[3/3] Restarting backend service..."
docker compose -f "$SCRIPT_DIR/docker-compose.yml" up -d
echo "      Done — backend restarted."

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "============================================================"
echo " Deployment complete!"
echo ""
echo "  Backend API (internal): http://127.0.0.1:8000"
echo "  Health check:           curl http://127.0.0.1:8000/api/health"
echo "  Container logs:         docker compose logs -f backend"
echo ""
echo "  Frontend files:         $WEB_ROOT"
echo "  Nginx must be running and configured — see deployment/nginx.conf.example"
echo "============================================================"

#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# Tuvima Library — container startup script
#
# Launches the Engine (port 61495) and Dashboard (port 5016) as background
# processes, then waits.  If either exits unexpectedly the script exits too,
# which causes Docker to restart the container (when restart: unless-stopped).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ── First-run: seed default config into the mounted /config volume ────────────
# If /config is empty (a fresh install or a brand-new volume mount), copy the
# example configs in so the Engine can start without manual setup.
if [ -d "/app/engine/config.example" ] && [ -z "$(ls -A /config 2>/dev/null)" ]; then
    echo "[Tuvima] /config is empty — seeding default configs from config.example/ ..."
    cp -r /app/engine/config.example/. /config/
fi

# ── Resolve paths (env vars > defaults) ─────────────────────────────────────
export TUVIMA_CONFIG_DIR="${TUVIMA_CONFIG_DIR:-/config}"
export TUVIMA_DB_PATH="${TUVIMA_DB_PATH:-/db/library.db}"
export TUVIMA_WATCH_FOLDER="${TUVIMA_WATCH_FOLDER:-/watch}"
export TUVIMA_LIBRARY_ROOT="${TUVIMA_LIBRARY_ROOT:-/library}"

# Dashboard → Engine address.  Inside one container both are on localhost.
# If running Engine and Dashboard as separate containers (advanced), set this
# to the Engine container's address, e.g. http://engine:61495
export TUVIMA_ENGINE_URL="${TUVIMA_ENGINE_URL:-http://localhost:61495}"

# Allow extra CORS origins so the Dashboard can reach the Engine.
# Always include localhost (same-container) plus any host IP set by the user.
export TUVIMA_CORS_ORIGINS="${TUVIMA_CORS_ORIGINS:-http://localhost:5016}"

echo "============================================================"
echo " Tuvima Library"
echo "------------------------------------------------------------"
echo " Engine  → http://0.0.0.0:61495"
echo " Dashboard → http://0.0.0.0:5016"
echo " Config  : $TUVIMA_CONFIG_DIR"
echo " Database: $TUVIMA_DB_PATH"
echo " Watch   : $TUVIMA_WATCH_FOLDER"
echo " Library : $TUVIMA_LIBRARY_ROOT"
echo "============================================================"

# ── Start Engine ──────────────────────────────────────────────────────────────
ASPNETCORE_URLS="http://+:61495" \
ASPNETCORE_ENVIRONMENT="Production" \
dotnet /app/engine/MediaEngine.Api.dll &
ENGINE_PID=$!

# Brief pause to allow the Engine to start accepting connections before the
# Dashboard's first request (avoids "connection refused" in the browser on
# the very first page load).
sleep 3

# ── Start Dashboard ───────────────────────────────────────────────────────────
ASPNETCORE_URLS="http://+:5016" \
ASPNETCORE_ENVIRONMENT="Production" \
Engine__BaseUrl="$TUVIMA_ENGINE_URL" \
dotnet /app/dashboard/MediaEngine.Web.dll &
DASHBOARD_PID=$!

echo "[Tuvima] Both services started.  Engine PID=$ENGINE_PID  Dashboard PID=$DASHBOARD_PID"

# ── Wait: exit as soon as either process stops ────────────────────────────────
wait -n $ENGINE_PID $DASHBOARD_PID
EXIT_CODE=$?

echo "[Tuvima] A process exited with code $EXIT_CODE — shutting down."
kill "$ENGINE_PID" "$DASHBOARD_PID" 2>/dev/null || true

exit $EXIT_CODE

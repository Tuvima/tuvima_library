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
# committed configs so the Engine can start without manual setup.
if [ -z "$(ls -A /config 2>/dev/null)" ]; then
    echo "[Tuvima] /config is empty — seeding default configs ..."
    cp -r /app/engine/config/. /config/
fi

# ── Resolve paths (env vars > defaults) ─────────────────────────────────────
export TUVIMA_CONFIG_DIR="${TUVIMA_CONFIG_DIR:-/config}"
export TUVIMA_DB_PATH="${TUVIMA_DB_PATH:-/db/library.db}"
export TUVIMA_WATCH_FOLDER="${TUVIMA_WATCH_FOLDER:-/watch}"
export TUVIMA_LIBRARY_ROOT="${TUVIMA_LIBRARY_ROOT:-/library}"
export TUVIMA_MODELS_DIR="${TUVIMA_MODELS_DIR:-/models}"

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
echo " Models  : $TUVIMA_MODELS_DIR"
echo "============================================================"

# ── Start Engine ──────────────────────────────────────────────────────────────
ASPNETCORE_URLS="http://+:61495" \
ASPNETCORE_ENVIRONMENT="Production" \
dotnet /app/engine/MediaEngine.Api.dll &
ENGINE_PID=$!

echo "[Tuvima] Waiting for Engine readiness on http://localhost:61495 ..."
ENGINE_READY_TIMEOUT_SECONDS="${TUVIMA_ENGINE_READY_TIMEOUT_SECONDS:-120}"
ENGINE_READY_DEADLINE=$((SECONDS + ENGINE_READY_TIMEOUT_SECONDS))

while true; do
    if ! kill -0 "$ENGINE_PID" 2>/dev/null; then
        wait "$ENGINE_PID" || ENGINE_EXIT_CODE=$?
        echo "[Tuvima] Engine exited before becoming ready. Exit code=${ENGINE_EXIT_CODE:-unknown}"
        exit "${ENGINE_EXIT_CODE:-1}"
    fi

    if (exec 3<>/dev/tcp/127.0.0.1/61495) 2>/dev/null; then
        exec 3>&-
        exec 3<&-
        echo "[Tuvima] Engine is reachable."
        break
    fi

    if [ "$SECONDS" -ge "$ENGINE_READY_DEADLINE" ]; then
        echo "[Tuvima] Engine did not become reachable within ${ENGINE_READY_TIMEOUT_SECONDS}s; shutting down."
        kill "$ENGINE_PID" 2>/dev/null || true
        wait "$ENGINE_PID" 2>/dev/null || true
        exit 1
    fi

    sleep 1
done

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

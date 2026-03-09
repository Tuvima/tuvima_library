#!/bin/bash
set -e

echo "┌──────────────────────────────────────────┐"
echo "│         Tuvima Library Starting           │"
echo "│  Engine:    http://localhost:8080          │"
echo "│  Dashboard: http://localhost:8081          │"
echo "└──────────────────────────────────────────┘"

# Start Engine in the background
ASPNETCORE_URLS=http://+:8080 dotnet /app/engine/MediaEngine.Api.dll &
ENGINE_PID=$!

# Wait for Engine to be ready before starting Dashboard
echo "Waiting for Engine to start..."
for i in $(seq 1 30); do
    if curl -sf http://localhost:8080/system/status > /dev/null 2>&1; then
        echo "Engine is ready."
        break
    fi
    if ! kill -0 $ENGINE_PID 2>/dev/null; then
        echo "Engine process exited unexpectedly."
        exit 1
    fi
    sleep 1
done

# Start Dashboard in the background
ASPNETCORE_URLS=http://+:8081 dotnet /app/dashboard/MediaEngine.Web.dll &
DASHBOARD_PID=$!

echo "Dashboard started. Tuvima Library is running."

# If either process exits, shut down the container
wait -n $ENGINE_PID $DASHBOARD_PID
EXIT_CODE=$?

echo "A service exited with code $EXIT_CODE. Shutting down..."
kill $ENGINE_PID $DASHBOARD_PID 2>/dev/null || true
wait
exit $EXIT_CODE

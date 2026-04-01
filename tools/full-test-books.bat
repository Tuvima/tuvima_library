@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: full-test-books.bat
:: Wipes the database, clears the watch folder, seeds BOOKS only,
:: and kicks off the ingestion pipeline.
::
:: Usage:  full-test-books.bat
:: ─────────────────────────────────────────────────────────────────────────────

set ENGINE_URL=http://localhost:61495
set TYPES=books

echo.
echo  FULL TEST — BOOKS ONLY
echo  Engine: %ENGINE_URL%
echo ─────────────────────────────────────────────────────────────────────────────
echo.

echo [1/2] Checking engine is reachable...
curl -sf "%ENGINE_URL%/system/status" >nul 2>&1
if errorlevel 1 (
    echo  ERROR: Engine not responding at %ENGINE_URL%
    echo  Start the engine first:  dotnet run --project src\MediaEngine.Api --launch-profile "MediaEngine.Api"
    echo.
    pause
    exit /b 1
)
echo  OK

echo.
echo [2/2] Running full test (wipe + seed books + scan)...
echo.
curl -s -X POST "%ENGINE_URL%/dev/full-test?types=%TYPES%" ^
     -H "Accept: application/json" ^
     | powershell -NoProfile -Command "$input | ConvertFrom-Json | ConvertTo-Json -Depth 5"

echo.
echo ─────────────────────────────────────────────────────────────────────────────
echo  Monitor progress:
echo    Batches   : GET %ENGINE_URL%/ingestion/batches
echo    Registry  : GET %ENGINE_URL%/registry/items?page=1^&pageSize=50
echo    Review    : GET %ENGINE_URL%/review/pending
echo    Activity  : GET %ENGINE_URL%/activity/recent
echo ─────────────────────────────────────────────────────────────────────────────
echo.
pause

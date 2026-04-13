@echo off
setlocal

set ENGINE_URL=http://localhost:61495

echo.
echo  Tuvima Library - Full Integration Harness
echo  Engine: %ENGINE_URL%
echo.
echo  WARNING: This will wipe the active runtime's database, watch folders,
echo           and organized library before running a fresh all-media ingestion.
echo.
echo  Press Ctrl+C to cancel, or any key to continue...
pause >nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-FullIntegration.ps1" -EngineUrl "%ENGINE_URL%"
if errorlevel 1 exit /b %errorlevel%

echo.
pause

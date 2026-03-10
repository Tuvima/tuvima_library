@echo off
setlocal

REM ─────────────────────────────────────────────────────────────────────────
REM  build-installer.bat — builds self-contained win-x64 binaries then runs
REM  Inno Setup to produce dist\TuvimaLibrary-Setup-{version}.exe
REM
REM  Prerequisites
REM    - .NET 10 SDK  (dotnet publish)
REM    - Inno Setup 6  (ISCC.exe on PATH, or set ISCC= env var)
REM ─────────────────────────────────────────────────────────────────────────

:: Locate ISCC (Inno Setup compiler)
if "%ISCC%"=="" (
    if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
        set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    ) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
        set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    ) else (
        echo ERROR: Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php
        echo        or set the ISCC environment variable to point to ISCC.exe
        exit /b 1
    )
)

:: Output directories
set "ENGINE_OUT=dist\win\engine"
set "DASHBOARD_OUT=dist\win\dashboard"

echo.
echo ── Cleaning previous build output ──────────────────────────────────────
if exist dist\win rmdir /s /q dist\win

echo.
echo ── Publishing Engine (win-x64 self-contained) ───────────────────────────
dotnet publish src\MediaEngine.Api\MediaEngine.Api.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%ENGINE_OUT%" ^
    -p:PublishSingleFile=false ^
    -p:UseAppHost=true
if errorlevel 1 (
    echo ERROR: Engine publish failed.
    exit /b 1
)

echo.
echo ── Publishing Dashboard (win-x64 self-contained) ────────────────────────
dotnet publish src\MediaEngine.Web\MediaEngine.Web.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%DASHBOARD_OUT%" ^
    -p:PublishSingleFile=false ^
    -p:UseAppHost=true
if errorlevel 1 (
    echo ERROR: Dashboard publish failed.
    exit /b 1
)

echo.
echo ── Running Inno Setup compiler ──────────────────────────────────────────
"%ISCC%" installer.iss
if errorlevel 1 (
    echo ERROR: Inno Setup compilation failed.
    exit /b 1
)

echo.
echo ── Done ─────────────────────────────────────────────────────────────────
echo Installer written to:  dist\TuvimaLibrary-Setup-*.exe

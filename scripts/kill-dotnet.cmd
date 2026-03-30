@echo off
:: Kill all .NET processes (dotnet.exe, MediaEngine.Api.exe, MediaEngine.Web.exe)
taskkill /F /IM dotnet.exe 2>nul
taskkill /F /IM MediaEngine.Api.exe 2>nul
taskkill /F /IM MediaEngine.Web.exe 2>nul
echo All .NET processes killed.

@echo off
setlocal
title DKay Game Server Dock Update

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Requesting administrator access...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-DKayGameServerDock.ps1"
set update_exit=%errorlevel%
echo.
if not "%update_exit%"=="0" echo Update failed with exit code %update_exit%.
pause
exit /b %update_exit%

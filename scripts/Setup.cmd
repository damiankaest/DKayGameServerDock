@echo off
setlocal
title DKay Game Server Dock Setup

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Requesting administrator access...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-DKayGameServerDock.ps1"
set setup_exit=%errorlevel%
echo.
if not "%setup_exit%"=="0" echo Setup failed with exit code %setup_exit%.
pause
exit /b %setup_exit%

@echo off
REM Tier 0 (docs/HOSTING.md): double-click this to start everything - backend
REM + dedicated game server - on this machine. See start-host.ps1 for the
REM actual logic; this .bat exists only because plain .ps1 files don't run
REM on double-click in Windows Explorer by default.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-host.ps1"

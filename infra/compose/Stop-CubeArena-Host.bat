@echo off
REM Tier 0 (docs/HOSTING.md): double-click this when you're done hosting -
REM stops the dedicated game server and brings the docker compose stack
REM down, so nothing keeps listening on the forwarded ports. See
REM stop-host.ps1 for the actual logic; this .bat exists only because plain
REM .ps1 files don't run on double-click in Windows Explorer by default.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop-host.ps1"

# Tier 0 (docs/HOSTING.md): one-click host teardown, the counterpart to
# start-host.ps1. Stops the dedicated game server process (if running) and
# brings the docker compose stack down, so nothing is still listening on
# the forwarded ports (7777/UDP, 8080/TCP) once you're done hosting -
# closing the server window alone (start-host.ps1's normal exit path)
# disconnects players but leaves Postgres/API running and still reachable.
#
# Meant to be launched via Stop-CubeArena-Host.bat, not double-clicked
# directly - see start-host.ps1's header comment for why.

Set-Location $PSScriptRoot

Write-Host "=== Cube Arena host teardown ===" -ForegroundColor Cyan
Write-Host ""

$serverProcesses = Get-Process CubeArena -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "*WindowsServer*" }
if ($serverProcesses) {
    Write-Host "Stopping the dedicated game server (disconnects anyone currently playing)..." -ForegroundColor Cyan
    $serverProcesses | Stop-Process -Force
} else {
    Write-Host "Dedicated game server is not running." -ForegroundColor DarkGray
}

Write-Host "Stopping the backend (Postgres + API)..." -ForegroundColor Cyan
docker compose down
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose down reported an error - check Docker Desktop." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host ""
Write-Host "Everything's stopped. Ports 7777/UDP and 8080/TCP are no longer listening -" -ForegroundColor Green
Write-Host "safe to leave your router's port forwarding rules in place between sessions." -ForegroundColor Green
Read-Host "Press Enter to close"

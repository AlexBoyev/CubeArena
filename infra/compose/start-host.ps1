# Tier 0 (docs/HOSTING.md): one-click host launcher. Brings up Postgres +
# API via docker compose, builds the dedicated server if it hasn't been
# built yet, waits for the API to report healthy, prints the connect
# string, then runs the dedicated server in the foreground.
#
# Meant to be launched via Start-CubeArena-Host.bat, not double-clicked
# directly - plain .ps1 files don't run on double-click in Windows
# Explorer by default (they open in a text editor instead).

param(
    [string]$UnityPath = "F:\Unity\6000.5.5f1\Editor\Unity.exe"
)

Set-Location $PSScriptRoot
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$serverExe = Join-Path $repoRoot "Builds\WindowsServer\CubeArena.exe"

Write-Host "=== Cube Arena host launcher ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path ".env")) {
    Write-Host "No .env found - copy .env.example to .env and set PUBLIC_HOST first (see docs/HOSTING.md Tier 0)." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host "Starting backend (Postgres + API)..." -ForegroundColor Cyan
docker compose up -d postgres api
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to start the backend - is Docker Desktop running?" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host "Waiting for the API to become healthy..." -ForegroundColor Cyan
$healthy = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:8080/health/ready" -UseBasicParsing -TimeoutSec 2
        if ($resp.StatusCode -eq 200) { $healthy = $true; break }
    } catch {}
    Start-Sleep -Seconds 2
}
if (-not $healthy) {
    Write-Host "API did not become healthy in time. Check 'docker compose logs api'." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}
Write-Host "Backend is up." -ForegroundColor Green
Write-Host ""

if (-not (Test-Path $serverExe)) {
    Write-Host "No dedicated server build found - building it now (first run only, takes a few minutes)..." -ForegroundColor Yellow
    if (Get-Process Unity -ErrorAction SilentlyContinue) {
        Write-Host "Unity Editor is currently running - close it first, then re-run this launcher." -ForegroundColor Red
        Read-Host "Press Enter to close"
        exit 1
    }
    $buildLog = Join-Path $repoRoot "host_launcher_build.log"
    Remove-Item $buildLog -ErrorAction SilentlyContinue
    Start-Process -FilePath $UnityPath -ArgumentList @(
        "-batchmode", "-quit",
        "-projectPath", $repoRoot,
        "-executeMethod", "BuildScript.BuildWindowsDedicatedServer",
        "-logFile", $buildLog
    ) -Wait
    if (-not (Test-Path $serverExe)) {
        Write-Host "Build failed - check $buildLog for errors." -ForegroundColor Red
        Read-Host "Press Enter to close"
        exit 1
    }
    Remove-Item $buildLog -ErrorAction SilentlyContinue
    Write-Host "Server build succeeded." -ForegroundColor Green
    Write-Host ""
}

& (Join-Path $PSScriptRoot "print-connect-info.ps1")

Write-Host ""
Write-Host "Starting the dedicated game server. Leave this window open while hosting -" -ForegroundColor Cyan
Write-Host "closing it (or Ctrl+C) stops the server and disconnects everyone." -ForegroundColor Cyan
Write-Host ""

& (Join-Path $PSScriptRoot "run-gameserver-native.ps1")

Read-Host "Server stopped. Press Enter to close"

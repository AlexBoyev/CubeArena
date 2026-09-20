# Tier 0 (docs/HOSTING.md): runs the dedicated game server as a native Windows
# process, reading the same PUBLIC_HOST/FLEET_API_KEY values from .env that
# the compose stack's api service uses. Exists because this dev machine has
# no Linux Dedicated Server Build Support (so it can't build/run
# infra/docker/Dockerfile.gameserver locally) and no GHCR pull access to the
# pre-built image (that package is currently private) - see CLAUDE.md.
#
# Prerequisite: build the server once via Unity batch mode -
#   Unity.exe -batchmode -quit -projectPath <repo root> -executeMethod BuildScript.BuildWindowsDedicatedServer
# (requires the Windows Dedicated Server Build Support module, installed via
# Unity Hub: `install-modules --version 6000.5.5f1 --module windows-server`)

$envPath = Join-Path $PSScriptRoot ".env"
if (-not (Test-Path $envPath)) {
    Write-Host "No .env found at $envPath - copy .env.example to .env first." -ForegroundColor Red
    exit 1
}

function Get-EnvValue([string]$name, [string]$default) {
    $line = Get-Content $envPath | Where-Object { $_ -match "^\s*$name\s*=" } | Select-Object -Last 1
    if (-not $line) { return $default }
    return ($line -split "=", 2)[1].Trim()
}

$publicHost = Get-EnvValue "PUBLIC_HOST" ""
$gameserverPort = Get-EnvValue "GAMESERVER_PORT" "7777"
$apiPort = Get-EnvValue "API_PORT" "8080"
$fleetApiKey = Get-EnvValue "FLEET_API_KEY" ""

if (-not $publicHost) {
    Write-Host "PUBLIC_HOST is not set in .env - see docs/HOSTING.md Tier 0." -ForegroundColor Red
    exit 1
}
if (-not $fleetApiKey) {
    Write-Host "FLEET_API_KEY is not set in .env." -ForegroundColor Red
    exit 1
}

$exePath = Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) "Builds\WindowsServer\CubeArena.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "No build found at $exePath - build it first (see this script's header comment)." -ForegroundColor Red
    exit 1
}

$env:CUBEARENA_BACKEND_URL = "http://localhost:$apiPort"
$env:CUBEARENA_FLEET_API_KEY = $fleetApiKey
$env:CUBEARENA_ADVERTISE_HOST = $publicHost
$env:CUBEARENA_LISTEN_PORT = $gameserverPort
$env:CUBEARENA_CAPACITY = "4"

Write-Host "Starting dedicated server: advertising $publicHost`:$gameserverPort, backend $($env:CUBEARENA_BACKEND_URL)" -ForegroundColor Cyan
& $exePath -batchmode -nographics

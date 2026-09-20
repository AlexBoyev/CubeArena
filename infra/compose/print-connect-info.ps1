# Tier 0 (docs/HOSTING.md): prints this machine's LAN IP alongside the
# PUBLIC_HOST actually configured in .env, and the exact connect string to
# hand out to players joining a LAN party. Run after `docker compose up -d`.

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
$apiPort = Get-EnvValue "API_PORT" "8080"
$gameserverPort = Get-EnvValue "GAMESERVER_PORT" "7777"

$detectedIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -ne "127.0.0.1" -and
        $_.PrefixOrigin -ne "WellKnown" -and
        $_.IPAddress -notlike "169.254.*"
    } |
    Select-Object -ExpandProperty IPAddress

Write-Host ""
Write-Host "=== Cube Arena - LAN connect info ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Detected LAN IP(s) on this machine:"
if ($detectedIps) {
    $detectedIps | ForEach-Object { Write-Host "  - $_" }
} else {
    Write-Host "  (none found - is this machine on a network?)" -ForegroundColor Yellow
}
Write-Host ""

if (-not $publicHost) {
    Write-Host "PUBLIC_HOST is not set in .env - set it to one of the IPs above." -ForegroundColor Red
    exit 1
}

if ($detectedIps -and ($detectedIps -notcontains $publicHost)) {
    Write-Host "WARNING: PUBLIC_HOST ($publicHost) doesn't match any IP detected on this machine right now." -ForegroundColor Yellow
    Write-Host "         If your LAN IP changed (e.g. DHCP lease renewal), update PUBLIC_HOST in .env." -ForegroundColor Yellow
    Write-Host ""
}

$connectString = "http://${publicHost}:${apiPort}"

Write-Host "Configured PUBLIC_HOST: $publicHost"
Write-Host "Backend API:            $connectString"
Write-Host "Game server UDP port:   ${publicHost}:${gameserverPort}"
Write-Host ""
Write-Host "Tell each player to paste this into the client's 'server address' field at login:" -ForegroundColor Green
Write-Host ""
Write-Host "  $connectString" -ForegroundColor White
Write-Host ""

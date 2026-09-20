# Tier 0 (docs/HOSTING.md): builds the Windows client and packages it into a
# single .zip you can hand to players - no Unity install needed on their end.
#
# Usage: .\package-client.ps1 [-UnityPath <path to Unity.exe>]

param(
    [string]$UnityPath = "F:\Unity\6000.5.5f1\Editor\Unity.exe"
)

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$clientDir = Join-Path $repoRoot "Builds\WindowsClient"
$buildLog = Join-Path $repoRoot "client_package_build.log"

if (Get-Process Unity -ErrorAction SilentlyContinue) {
    Write-Host "Unity Editor is currently running - close it first (batch-mode builds can't run alongside it)." -ForegroundColor Red
    exit 1
}

Write-Host "Building the Windows client via Unity batch mode..." -ForegroundColor Cyan
Remove-Item $buildLog -ErrorAction SilentlyContinue
$proc = Start-Process -FilePath $UnityPath -ArgumentList @(
    "-batchmode", "-quit",
    "-projectPath", $repoRoot,
    "-executeMethod", "BuildScript.BuildWindowsClient",
    "-logFile", $buildLog
) -PassThru -Wait

if (-not (Test-Path (Join-Path $clientDir "CubeArena.exe"))) {
    Write-Host "Build did not produce CubeArena.exe - check $buildLog for errors." -ForegroundColor Red
    exit 1
}

$resultLine = Select-String -Path $buildLog -Pattern "Client build succeeded|Build result:" | Select-Object -Last 1
Write-Host "Build finished: $resultLine" -ForegroundColor Green

# Drop a README into the build folder with today's connect string, pulled
# from .env - a friend running this zip has no other way to know it.
$envPath = Join-Path $PSScriptRoot ".env"
$publicHost = ""
$apiPort = "8080"
if (Test-Path $envPath) {
    $envLines = Get-Content $envPath
    $hostLine = $envLines | Where-Object { $_ -match "^\s*PUBLIC_HOST\s*=" } | Select-Object -Last 1
    if ($hostLine) { $publicHost = ($hostLine -split "=", 2)[1].Trim() }
    $portLine = $envLines | Where-Object { $_ -match "^\s*API_PORT\s*=" } | Select-Object -Last 1
    if ($portLine) { $apiPort = ($portLine -split "=", 2)[1].Trim() }
}
$connectString = if ($publicHost) { "http://${publicHost}:${apiPort}" } else { "(ask whoever sent you this for the server address)" }

$readme = "Cube Arena - quick start`r`n`r`n" +
    "1. Run CubeArena.exe.`r`n" +
    "2. On the login screen, paste this into the `"server address`" field:`r`n`r`n" +
    "   $connectString`r`n`r`n" +
    "3. Register an account (any email/password - it's a local test server),`r`n" +
    "   then log in.`r`n" +
    "4. Click Quick Play.`r`n`r`n" +
    "If it doesn't connect, ask the host to double-check the server address`r`n" +
    "above is still current (it can change if their network address changes).`r`n"

Set-Content -Path (Join-Path $clientDir "README.txt") -Value $readme -NoNewline -Encoding ascii

# Package into a single .zip.
$buildsDir = Join-Path $repoRoot "Builds"
$zipPath = Join-Path $buildsDir "CubeArena-Client.zip"
Remove-Item $zipPath -ErrorAction SilentlyContinue
Compress-Archive -Path "$clientDir\*" -DestinationPath $zipPath

Remove-Item $buildLog -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done. Share this one file:" -ForegroundColor Green
Write-Host "  $zipPath" -ForegroundColor White
Write-Host "($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB)"

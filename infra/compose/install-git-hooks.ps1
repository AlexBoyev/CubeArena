# Installs this repo's tracked git hooks (infra/compose/hooks/) into
# .git/hooks/ - git never tracks .git/hooks/ itself, so this is the
# reproducible way to set them up on a fresh clone. Run once after cloning.
#
# Currently installs: pre-push (rebuilds the client package in the
# background after every push - see infra/compose/package-client.ps1 and
# docs/HOSTING.md).

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hooksSourceDir = Join-Path $PSScriptRoot "hooks"
$hooksTargetDir = Join-Path $repoRoot ".git\hooks"

if (-not (Test-Path $hooksTargetDir)) {
    Write-Host "No .git/hooks directory found - is this a git repository?" -ForegroundColor Red
    exit 1
}

Get-ChildItem $hooksSourceDir -File | ForEach-Object {
    $target = Join-Path $hooksTargetDir $_.Name
    Copy-Item $_.FullName $target -Force
    Write-Host "Installed hook: $($_.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. These hooks now run automatically on the relevant git actions." -ForegroundColor Green

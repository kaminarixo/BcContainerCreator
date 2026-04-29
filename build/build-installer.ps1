<#
.SYNOPSIS
    Baut das Single-File-Publish + den Inno-Setup-Installer.

.DESCRIPTION
    1. dotnet publish src/BcContainerCreator.App  →  dist/publish/
    2. ISCC.exe installer/BcContainerCreator.iss  →  dist/BcContainerCreator-Setup-<version>.exe

.PARAMETER Version
    Optional — überschreibt die Version im .iss-Script via -DMyAppVersion.
    Default: 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.1'
)

$ErrorActionPreference = 'Stop'

$repoRoot     = Split-Path -Parent $PSScriptRoot
$appProject   = Join-Path $repoRoot 'src\BcContainerCreator.App'
$publishDir   = Join-Path $repoRoot 'dist\publish'
$issScript    = Join-Path $repoRoot 'installer\BcContainerCreator.iss'
$distDir      = Join-Path $repoRoot 'dist'

# 1. publish-Ordner sauber anlegen
if (Test-Path $publishDir) {
    Write-Host "Aufräumen: $publishDir"
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# 2. dotnet publish (framework-dependent, single-file)
Write-Host ""
Write-Host "=== dotnet publish ==="
& dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    -p:PublishSingleFile=true `
    --self-contained false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ist fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

# 3. ISCC.exe finden
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 nicht gefunden." -ForegroundColor Yellow
    Write-Host "Installiere via:  winget install --exact --id JRSoftware.InnoSetup --silent" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Publish-Output liegt unter: $publishDir" -ForegroundColor Yellow
    exit 1
}

# 4. ISCC compile
Write-Host ""
Write-Host "=== ISCC compile ==="
Write-Host "ISCC: $iscc"
Write-Host "Script: $issScript"
& $iscc "/DMyAppVersion=$Version" $issScript
if ($LASTEXITCODE -ne 0) {
    throw "ISCC ist fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

# 5. Result
$setupFile = Get-ChildItem -Path $distDir -Filter "BcContainerCreator-Setup-*.exe" |
             Sort-Object LastWriteTime -Descending |
             Select-Object -First 1

if ($setupFile) {
    $sizeMb = [Math]::Round($setupFile.Length / 1MB, 1)
    Write-Host ""
    Write-Host "=== Fertig ===" -ForegroundColor Green
    Write-Host "$($setupFile.FullName)  ($sizeMb MB)" -ForegroundColor Green
}

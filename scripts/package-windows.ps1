[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishedRoot = Join-Path $repositoryRoot "artifacts\$Runtime"
$packageRoot = Join-Path $repositoryRoot "artifacts\package-$Runtime"
$payloadRoot = Join-Path $packageRoot 'payload'
$archivePath = Join-Path $repositoryRoot "artifacts\DKayGameServerDock-$Runtime.zip"
$checksumPath = "$archivePath.sha256"

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -Runtime $Runtime -Configuration $Configuration -SelfContained $true
}

if (-not (Test-Path -LiteralPath (Join-Path $publishedRoot 'DKay.GameServerDock.Api.exe') -PathType Leaf)) {
    throw "Published Windows payload is missing at '$publishedRoot'."
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

Copy-Item -Path (Join-Path $publishedRoot '*') -Destination $payloadRoot -Recurse -Force
foreach ($file in @('Setup.cmd', 'Setup-DKayGameServerDock.ps1', 'install-windows-service.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $packageRoot $file) -Force
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\install-windows.md') -Destination (Join-Path $packageRoot 'README-INSTALL.md') -Force

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host "Windows setup package: $archivePath"
Write-Host "SHA-256: $hash"

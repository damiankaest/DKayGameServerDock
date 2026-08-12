[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [switch]$KeepDownload
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Save this script to disk and run it as Administrator.'
    }
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Version `"$Version`""
    if ($KeepDownload) { $arguments += ' -KeepDownload' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    return
}

$repository = 'damiankaest/DKayGameServerDock'
$headers = @{ 'User-Agent' = 'DKayGameServerDock-Installer' }
$releaseUri = if ($Version -eq 'latest') {
    "https://api.github.com/repos/$repository/releases/latest"
}
else {
    "https://api.github.com/repos/$repository/releases/tags/$Version"
}

Write-Host "Resolving DKay Game Server Dock release '$Version'..." -ForegroundColor Cyan
$release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -UseBasicParsing
$asset = $release.assets | Where-Object { $_.name -eq 'DKayGameServerDock-win-x64.zip' } | Select-Object -First 1
$checksumAsset = $release.assets | Where-Object { $_.name -eq 'DKayGameServerDock-win-x64.zip.sha256' } | Select-Object -First 1
if (-not $asset) {
    throw "Release '$($release.tag_name)' has no DKayGameServerDock-win-x64.zip asset."
}
if (-not $checksumAsset) {
    throw "Release '$($release.tag_name)' has no SHA-256 checksum asset."
}

$assetUri = [Uri]$asset.browser_download_url
$checksumAssetUri = [Uri]$checksumAsset.browser_download_url
if ($assetUri.Scheme -ne 'https' -or $assetUri.Host -ne 'github.com' -or
    $checksumAssetUri.Scheme -ne 'https' -or $checksumAssetUri.Host -ne 'github.com') {
    throw 'GitHub returned an unexpected release download URL.'
}

$downloadRoot = Join-Path $env:TEMP "DKayGameServerDock-$($release.tag_name)-$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $downloadRoot 'DKayGameServerDock-win-x64.zip'
$checksumPath = Join-Path $downloadRoot 'DKayGameServerDock-win-x64.zip.sha256'
$extractRoot = Join-Path $downloadRoot 'package'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null

try {
    Write-Host "Downloading $($release.tag_name)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $assetUri -Headers $headers -OutFile $archivePath -UseBasicParsing
    Invoke-WebRequest -Uri $checksumAssetUri -Headers $headers -OutFile $checksumPath -UseBasicParsing
    $checksumText = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($checksumText -notmatch '^([a-fA-F0-9]{64})\s+DKayGameServerDock-win-x64\.zip$') {
        throw 'The release checksum file has an unexpected format.'
    }
    $expectedHash = $Matches[1].ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw 'The downloaded release ZIP failed SHA-256 verification.'
    }
    Write-Host "SHA-256 verified: $actualHash" -ForegroundColor Green
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    $setupScript = Join-Path $extractRoot 'Setup-DKayGameServerDock.ps1'
    if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
        throw 'The release package is invalid: Setup-DKayGameServerDock.ps1 is missing.'
    }
    & $setupScript
}
finally {
    if ($KeepDownload) {
        Write-Host "Release files kept at $downloadRoot" -ForegroundColor DarkGray
    }
    elseif (Test-Path -LiteralPath $downloadRoot) {
        Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

[CmdletBinding()]
param(
    [string]$ServiceName = 'DKayGameServerDock',
    [string]$DisplayName = 'DKay Game Server Dock',
    [string]$InstallDirectory = 'C:\Program Files\DKayGameServerDock'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repositoryRoot 'artifacts\win-x64'
$executable = Join-Path $InstallDirectory 'DKay.GameServerDock.Api.exe'

if (-not (Test-Path $source)) {
    throw 'Publish artifacts are missing. Run publish-windows.ps1 first.'
}

if (-not (Test-Path $InstallDirectory)) {
    New-Item -ItemType Directory -Path $InstallDirectory | Out-Null
}
Copy-Item -Path (Join-Path $source '*') -Destination $InstallDirectory -Recurse -Force

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe config $ServiceName binPath= "`"$executable`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null
}
else {
    New-Service -Name $ServiceName -BinaryPathName "`"$executable`"" -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

Start-Service -Name $ServiceName
Write-Host "Service $ServiceName is installed and running."


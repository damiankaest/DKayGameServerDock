[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot 'src\web'
$apiRoot = Join-Path $repositoryRoot 'src\DKay.GameServerDock.Api'
$wwwRoot = Join-Path $apiRoot 'wwwroot'
$artifactRoot = Join-Path $repositoryRoot "artifacts\$Runtime"

Push-Location $webRoot
try {
    npm ci
    npm run build
}
finally {
    Pop-Location
}

if (Test-Path $wwwRoot) {
    Remove-Item -Path $wwwRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwRoot | Out-Null
Copy-Item -Path (Join-Path $webRoot 'dist\web\browser\*') -Destination $wwwRoot -Recurse -Force

dotnet publish (Join-Path $apiRoot 'DKay.GameServerDock.Api.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $artifactRoot

Write-Host "Published DKayGameServerDock to $artifactRoot"


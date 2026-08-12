[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [bool]$SelfContained = $true
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot 'src\web'
$apiRoot = Join-Path $repositoryRoot 'src\DKay.GameServerDock.Api'
$wwwRoot = Join-Path $apiRoot 'wwwroot'
$artifactRoot = Join-Path $repositoryRoot "artifacts\$Runtime"

foreach ($command in @('npm', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required build tool '$command' was not found on PATH."
    }
}

Push-Location $webRoot
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Angular build failed with exit code $LASTEXITCODE." }
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
    --self-contained $SelfContained `
    --output $artifactRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $artifactRoot 'DKay.GameServerDock.Api.exe') -PathType Leaf)) {
    throw "dotnet publish completed without creating the expected Windows executable."
}

Write-Host "Published DKayGameServerDock to $artifactRoot (self-contained: $SelfContained)"

[CmdletBinding()]
param(
    [switch]$SkipPull,
    [switch]$SkipBuild,
    [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Native([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        $renderedCommand = (@($Command) + $Arguments) -join ' '
        throw "Native command failed with exit code ${LASTEXITCODE}: $renderedCommand"
    }
}

function Get-ServiceEnvironment([string]$Name) {
    $values = (Get-ItemProperty `
        -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$Name" `
        -Name Environment `
        -ErrorAction Stop).Environment
    $result = @{}
    foreach ($entry in @($values)) {
        if ($entry -match '^([^=]+)=(.*)$') {
            $result[$Matches[1]] = $Matches[2]
        }
    }
    return $result
}

function Get-Setting([hashtable]$Settings, [string]$Name, [string]$Fallback) {
    if ($Settings.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($Settings[$Name])) {
        return $Settings[$Name]
    }
    return $Fallback
}

function Get-Port([hashtable]$Settings, [string]$Name, [int]$Fallback) {
    $raw = Get-Setting $Settings $Name $Fallback.ToString()
    $value = 0
    if (-not [int]::TryParse($raw, [ref]$value) -or $value -lt 1 -or $value -gt 65535) {
        throw "Stored service setting '$Name' is not a valid port: '$raw'."
    }
    return $value
}

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'The source updater must run on Windows.'
}

if (-not (Test-IsAdministrator)) {
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Save this script to disk and run it as Administrator.'
    }

    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($SkipPull) { $arguments += ' -SkipPull' }
    if ($SkipBuild) { $arguments += ' -SkipBuild' }
    if ($SkipBrowser) { $arguments += ' -SkipBrowser' }
    $elevated = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $elevated.ExitCode
}

$repositoryRoot = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git') -PathType Container)) {
    throw "'$repositoryRoot' is not a Git checkout. Run Update.cmd from the cloned repository."
}

$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$logPath = Join-Path $artifactDirectory "update-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$transcriptStarted = $false

try {
    Start-Transcript -LiteralPath $logPath -Force | Out-Null
    $transcriptStarted = $true

    Write-Host 'DKay Game Server Dock source update' -ForegroundColor Green
    Write-Host "Repository: $repositoryRoot"
    Write-Host "Log:        $logPath" -ForegroundColor DarkGray

    foreach ($command in @('git', 'npm', 'dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required update tool '$command' was not found on PATH."
        }
    }

    if (-not $SkipPull) {
        $changes = @(& git -C $repositoryRoot status --porcelain)
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not inspect the Git working tree.'
        }
        if ($changes.Count -gt 0) {
            throw "The Git working tree contains local changes. Commit or remove them before updating:`n$($changes -join "`n")"
        }

        $branch = ([string](& git -C $repositoryRoot branch --show-current)).Trim()
        if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
            throw "The updater only installs the tested main branch. Current branch: '$branch'."
        }

        Write-Host ''
        Write-Host '[1/4] Pull latest main' -ForegroundColor Cyan
        Invoke-Native 'git' @('-C', $repositoryRoot, 'pull', '--ff-only', 'origin', 'main')
    }
    else {
        Write-Host '[1/4] Git pull skipped.' -ForegroundColor Yellow
    }

    if (-not $SkipBuild) {
        Write-Host ''
        Write-Host '[2/4] Build self-contained Windows package' -ForegroundColor Cyan
        & (Join-Path $repositoryRoot 'scripts\package-windows.ps1')
    }
    else {
        Write-Host '[2/4] Build skipped; existing artifacts will be used.' -ForegroundColor Yellow
    }

    $payloadDirectory = Join-Path $artifactDirectory 'win-x64'
    if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory 'DKay.GameServerDock.Api.exe') -PathType Leaf)) {
        throw "The Windows payload is missing at '$payloadDirectory'. Run the update again without -SkipBuild."
    }

    Write-Host ''
    Write-Host '[3/4] Upgrade Windows service' -ForegroundColor Cyan
    $serviceName = 'DKayGameServerDock'
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Host 'No existing service was found; opening first-time setup.' -ForegroundColor Yellow
        & (Join-Path $artifactDirectory 'package-win-x64\Setup.cmd')
        if ($LASTEXITCODE -ne 0) {
            throw "First-time setup failed with exit code $LASTEXITCODE."
        }
        return
    }

    $settings = Get-ServiceEnvironment $serviceName
    $adminPort = Get-Port $settings 'DGS_ADMIN_PORT' 5080
    $publicPort = Get-Port $settings 'DGS_PUBLIC_PORTAL_PORT' 5081
    $publicEnabled = (Get-Setting $settings 'DGS_PUBLIC_PORTAL_ENABLED' 'false') -eq 'true'
    $displayName = 'DKay Game Server Dock'
    $lanFirewallEnabled = $null -ne (Get-NetFirewallRule -DisplayName "$displayName UI (LAN)" -ErrorAction SilentlyContinue)

    $installerParameters = @{
        ServiceName = $serviceName
        DisplayName = $displayName
        SourceDirectory = $payloadDirectory
        InstallDirectory = (Get-Setting $settings 'DGS_INSTALL_DIRECTORY' 'C:\Program Files\DKayGameServerDock')
        DataRoot = (Get-Setting $settings 'DGS_DATA_ROOT' 'C:\ProgramData\DKayGameServerDock')
        ServersRoot = (Get-Setting $settings 'DGS_SERVERS_ROOT' 'C:\GameServers')
        SteamCmdPath = (Get-Setting $settings 'DGS_STEAMCMD_PATH' '')
        JavaPath = (Get-Setting $settings 'DGS_JAVA_PATH' 'java')
        Port = $adminPort
        OpenLanFirewall = $lanFirewallEnabled
        EnablePublicPortal = $publicEnabled
        PublicPortalPort = $publicPort
        PublicHost = (Get-Setting $settings 'DGS_PUBLIC_HOST' '')
        PublicPortalName = (Get-Setting $settings 'DGS_PUBLIC_PORTAL_NAME' 'DKay Game Servers')
    }
    & (Join-Path $repositoryRoot 'scripts\install-windows-service.ps1') @installerParameters

    Write-Host ''
    Write-Host '[4/4] Verify installed version' -ForegroundColor Cyan
    $commit = ([string](& git -C $repositoryRoot rev-parse --short HEAD)).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the installed Git commit.' }
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:$adminPort/health" -TimeoutSec 5
    if ($health.status -ne 'healthy') {
        throw "Health endpoint returned unexpected status '$($health.status)'."
    }

    Write-Host ''
    Write-Host "Update complete: commit $commit is healthy on port $adminPort." -ForegroundColor Green
    if (-not $SkipBrowser) {
        Start-Process "http://localhost:$adminPort"
    }
}
catch {
    Write-Host ''
    Write-Host "UPDATE FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Detailed log: $logPath" -ForegroundColor Yellow
    throw
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}

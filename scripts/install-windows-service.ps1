#Requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$ServiceName = 'DKayGameServerDock',
    [string]$DisplayName = 'DKay Game Server Dock',
    [string]$Runtime = 'win-x64',
    [string]$SourceDirectory = '',
    [string]$InstallDirectory = 'C:\Program Files\DKayGameServerDock',
    [string]$DataRoot = 'C:\ProgramData\DKayGameServerDock',
    [string]$ServersRoot = 'C:\GameServers',
    [string]$SteamCmdPath = '',
    [string]$JavaPath = 'java',
    [ValidateSet('NT AUTHORITY\LocalService')]
    [string]$ServiceAccount = 'NT AUTHORITY\LocalService',
    [int]$Port = 5080,
    [switch]$OpenLanFirewall,
    [switch]$EnablePublicPortal,
    [int]$PublicPortalPort = 5081,
    [string]$PublicHost = '',
    [string]$PublicPortalName = 'DKay Game Servers'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $repositoryRoot "artifacts\$Runtime"
}

$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$ServersRoot = [IO.Path]::GetFullPath($ServersRoot)
$executable = Join-Path $InstallDirectory 'DKay.GameServerDock.Api.exe'

function Assert-Port([string]$Name, [int]$Value) {
    if ($Value -lt 1 -or $Value -gt 65535) {
        throw "$Name must be between 1 and 65535."
    }
}

function Assert-SafeDirectory([string]$Name, [string]$Path) {
    $normalized = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root = [IO.Path]::GetPathRoot($normalized).TrimEnd('\')
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $protected = @(
        $root,
        $env:SystemRoot,
        $env:ProgramFiles,
        $programFilesX86,
        $env:ProgramData
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.TrimEnd('\') }
    if ($protected -contains $normalized) {
        throw "$Name cannot be a drive, Windows, Program Files or ProgramData root: '$Path'."
    }
}

function Test-PathsOverlap([string]$First, [string]$Second) {
    $firstPath = [IO.Path]::GetFullPath($First).TrimEnd('\') + '\'
    $secondPath = [IO.Path]::GetFullPath($Second).TrimEnd('\') + '\'
    return $firstPath.StartsWith($secondPath, [StringComparison]::OrdinalIgnoreCase) -or
        $secondPath.StartsWith($firstPath, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-ServiceControl([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments) {
    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed with exit code $LASTEXITCODE while running: $($Arguments -join ' ')"
    }
}

function Grant-DirectoryAccess([string]$Path, [string]$Account, [string]$Permission) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /grant "${Account}:(OI)(CI)$Permission" /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant $Permission access to '$Path' for '$Account'."
    }
}

Assert-Port 'Administrator port' $Port
Assert-Port 'Public portal port' $PublicPortalPort
Assert-SafeDirectory 'Source directory' $SourceDirectory
Assert-SafeDirectory 'Application directory' $InstallDirectory
Assert-SafeDirectory 'Application data directory' $DataRoot
Assert-SafeDirectory 'Game server directory' $ServersRoot
if (Test-PathsOverlap $SourceDirectory $InstallDirectory) {
    throw 'The release source and application directory must not overlap.'
}
if (Test-PathsOverlap $InstallDirectory $DataRoot) {
    throw 'The application and data directories must not overlap.'
}
if (Test-PathsOverlap $InstallDirectory $ServersRoot) {
    throw 'The application and game-server directories must not overlap.'
}
if (Test-PathsOverlap $DataRoot $ServersRoot) {
    throw 'The application-data and game-server directories must not overlap.'
}
if ($EnablePublicPortal) {
    if ([string]::IsNullOrWhiteSpace($PublicHost)) {
        throw 'PublicHost is required when EnablePublicPortal is set. Use a MyFRITZ/DynDNS name or public IP address.'
    }
    $normalizedPublicHost = $PublicHost.Trim().Trim('[', ']')
    if ([Uri]::CheckHostName($normalizedPublicHost) -eq [UriHostNameType]::Unknown) {
        throw 'PublicHost must be a valid MyFRITZ/DynDNS name or public IP address without a URL scheme.'
    }
    if ($PublicPortalPort -eq $Port) {
        throw 'The public guest portal and the administrator panel must use different ports.'
    }
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Publish artifacts are missing at '$SourceDirectory'. Build or download the Windows release package first."
}
if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory 'DKay.GameServerDock.Api.exe') -PathType Leaf)) {
    throw "'$SourceDirectory' is not a valid DKayGameServerDock Windows payload."
}

$listenUrls = "http://0.0.0.0:$Port"
if ($EnablePublicPortal) {
    $listenUrls = "$listenUrls;http://0.0.0.0:$PublicPortalPort"
}
$binaryPath = "`"$executable`" --urls `"$listenUrls`""

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service $ServiceName..."
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

if ($existing) {
    $databasePath = Join-Path $DataRoot 'dkay-game-server-dock.db'
    if (Test-Path -LiteralPath $databasePath -PathType Leaf) {
        $backupRoot = Join-Path $DataRoot 'setup-backups'
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backupPath = Join-Path $backupRoot "dkay-game-server-dock-$(Get-Date -Format 'yyyyMMdd-HHmmss').db"
        Copy-Item -LiteralPath $databasePath -Destination $backupPath -Force
        Write-Host "Database backup created at $backupPath"
    }
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$robocopyOutput = & robocopy.exe $SourceDirectory $InstallDirectory /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -gt 7) {
    throw "Application files could not be deployed (robocopy exit code $LASTEXITCODE): $robocopyOutput"
}

Grant-DirectoryAccess $InstallDirectory $ServiceAccount 'RX'
Grant-DirectoryAccess $DataRoot $ServiceAccount 'M'
Grant-DirectoryAccess $ServersRoot $ServiceAccount 'M'
if (-not [string]::IsNullOrWhiteSpace($SteamCmdPath) -and (Test-Path -LiteralPath $SteamCmdPath -PathType Leaf)) {
    Grant-DirectoryAccess (Split-Path -Parent $SteamCmdPath) $ServiceAccount 'M'
}
if (-not [string]::IsNullOrWhiteSpace($JavaPath) -and (Test-Path -LiteralPath $JavaPath -PathType Leaf)) {
    Grant-DirectoryAccess (Split-Path -Parent $JavaPath) $ServiceAccount 'RX'
}

if ($existing) {
    Invoke-ServiceControl config $ServiceName binPath= $binaryPath start= auto DisplayName= "`"$DisplayName`""
}
else {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

Invoke-ServiceControl config $ServiceName obj= $ServiceAccount
Invoke-ServiceControl description $ServiceName 'Self-hosted game server control panel'
Invoke-ServiceControl failure $ServiceName reset= 86400 actions= 'restart/5000/restart/15000/restart/60000'

$environmentKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$serviceEnvironment = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "DGS_INSTALL_DIRECTORY=$InstallDirectory",
    "DGS_ADMIN_PORT=$Port",
    "DGS_DATA_ROOT=$DataRoot",
    "DGS_SERVERS_ROOT=$ServersRoot",
    "DGS_STEAMCMD_PATH=$SteamCmdPath",
    "DGS_JAVA_PATH=$JavaPath",
    "DGS_PUBLIC_PORTAL_ENABLED=$($EnablePublicPortal.IsPresent.ToString().ToLowerInvariant())",
    "DGS_PUBLIC_PORTAL_PORT=$PublicPortalPort",
    "DGS_PUBLIC_HOST=$PublicHost",
    "DGS_PUBLIC_PORTAL_NAME=$PublicPortalName"
)
New-ItemProperty -Path $environmentKey -Name Environment -PropertyType MultiString -Value $serviceEnvironment -Force | Out-Null

$lanFirewallName = "$DisplayName UI (LAN)"
Get-NetFirewallRule -DisplayName $lanFirewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($OpenLanFirewall) {
    New-NetFirewallRule `
        -DisplayName $lanFirewallName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $Port `
        -RemoteAddress LocalSubnet | Out-Null
}

$guestFirewallName = "$DisplayName Guest Portal"
Get-NetFirewallRule -DisplayName $guestFirewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($EnablePublicPortal) {
    New-NetFirewallRule `
        -DisplayName $guestFirewallName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $PublicPortalPort | Out-Null
}

Start-Service -Name $ServiceName

$healthUrl = "http://127.0.0.1:$Port/health"
$deadline = (Get-Date).AddSeconds(60)
$lastError = $null
do {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 3
        if ($health.status -eq 'healthy') {
            Write-Host "Service $ServiceName is installed and healthy at $healthUrl"
            Write-Host "Service identity: $ServiceAccount"
            if ($OpenLanFirewall) {
                Write-Host "LAN administrator access is enabled on TCP $Port for LocalSubnet only."
            }
            if ($EnablePublicPortal) {
                Write-Host "Guest portal is enabled on TCP $PublicPortalPort for $PublicHost."
                Write-Host "Do not forward administrator port $Port."
            }
            return
        }
    }
    catch {
        $lastError = $_.Exception.Message
    }
    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
throw "Service health check failed after 60 seconds (state: $($service.Status), last error: $lastError). Check Windows Event Viewer and '$DataRoot'."

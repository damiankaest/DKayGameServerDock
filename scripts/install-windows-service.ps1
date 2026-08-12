#Requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$ServiceName = 'DKayGameServerDock',
    [string]$DisplayName = 'DKay Game Server Dock',
    [string]$Runtime = 'win-x64',
    [string]$InstallDirectory = 'C:\Program Files\DKayGameServerDock',
    [int]$Port = 5080,
    [switch]$OpenLanFirewall,
    [switch]$EnablePublicPortal,
    [int]$PublicPortalPort = 5081,
    [string]$PublicHost = '',
    [string]$PublicPortalName = 'DKay Game Servers'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repositoryRoot "artifacts\$Runtime"
$executable = Join-Path $InstallDirectory 'DKay.GameServerDock.Api.exe'
$listenUrls = "http://0.0.0.0:$Port"
if ($EnablePublicPortal) {
    if ([string]::IsNullOrWhiteSpace($PublicHost)) {
        throw 'PublicHost is required when EnablePublicPortal is set. Use a MyFRITZ/DynDNS name or public IP address.'
    }
    if ($PublicPortalPort -eq $Port) {
        throw 'The public guest portal and the administrator panel must use different ports.'
    }
    $listenUrls = "$listenUrls;http://0.0.0.0:$PublicPortalPort"
}
$binaryPath = "`"$executable`" --urls $listenUrls"

if (-not (Test-Path $source)) {
    throw "Publish artifacts are missing at '$source'. Run publish-windows.ps1 first."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service $ServiceName..."
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

if (-not (Test-Path $InstallDirectory)) {
    New-Item -ItemType Directory -Path $InstallDirectory | Out-Null
}

Copy-Item -Path (Join-Path $source '*') -Destination $InstallDirectory -Recurse -Force

if ($existing) {
    sc.exe config $ServiceName binPath= $binaryPath start= auto DisplayName= "`"$DisplayName`"" | Out-Null
}
else {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

sc.exe description $ServiceName "Self-hosted game server control panel" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

$environmentKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$serviceEnvironment = @(
    "DGS_PUBLIC_PORTAL_ENABLED=$($EnablePublicPortal.IsPresent.ToString().ToLowerInvariant())",
    "DGS_PUBLIC_PORTAL_PORT=$PublicPortalPort",
    "DGS_PUBLIC_HOST=$PublicHost",
    "DGS_PUBLIC_PORTAL_NAME=$PublicPortalName"
)
New-ItemProperty -Path $environmentKey -Name Environment -PropertyType MultiString -Value $serviceEnvironment -Force | Out-Null

if ($OpenLanFirewall) {
    $firewallName = "$DisplayName UI (LAN)"
    if (-not (Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule `
            -DisplayName $firewallName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $Port `
            -RemoteAddress LocalSubnet | Out-Null
    }
}

$guestFirewallName = "$DisplayName Guest Portal"
if ($EnablePublicPortal) {
    Get-NetFirewallRule -DisplayName $guestFirewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule `
        -DisplayName $guestFirewallName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $PublicPortalPort | Out-Null
}
else {
    Get-NetFirewallRule -DisplayName $guestFirewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}

Start-Service -Name $ServiceName

$healthUrl = "http://127.0.0.1:$Port/health"
$deadline = (Get-Date).AddSeconds(45)
$lastError = $null
do {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 3
        if ($health.status -eq 'healthy') {
            Write-Host "Service $ServiceName is installed and healthy at $healthUrl"
            if ($OpenLanFirewall) {
                Write-Host "A Windows Firewall rule for local-subnet access on TCP $Port is active."
            }
            if ($EnablePublicPortal) {
                Write-Host "Guest portal is listening on TCP $PublicPortalPort and its Windows Firewall rule is active."
                Write-Host "Create a FRITZ!Box port forwarding rule only for this port."
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
throw "Service health check failed after 45 seconds (state: $($service.Status), last error: $lastError). Check Windows Event Viewer and the service account permissions."

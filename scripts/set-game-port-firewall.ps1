#Requires -RunAsAdministrator

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [Parameter(Mandatory)]
    [ValidateSet('TCP', 'UDP', 'Both')]
    [string]$Protocol,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9 _.-]{1,80}$')]
    [string]$ServerName,

    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$protocols = if ($Protocol -eq 'Both') { @('TCP', 'UDP') } else { @($Protocol) }

foreach ($currentProtocol in $protocols) {
    $displayName = "DKay Game Server - $ServerName ($currentProtocol $Port)"
    $existing = Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue

    if ($Remove) {
        if ($existing) { $existing | Remove-NetFirewallRule }
        Write-Host "Removed firewall rule: $displayName"
        continue
    }

    if (-not $existing) {
        New-NetFirewallRule `
            -DisplayName $displayName `
            -Direction Inbound `
            -Action Allow `
            -Protocol $currentProtocol `
            -LocalPort $Port | Out-Null
    }
    Write-Host "Firewall rule active: $displayName"
}

Write-Host 'Windows Firewall is configured. Add or remove the matching explicit FRITZ!Box port forwarding rule separately.'

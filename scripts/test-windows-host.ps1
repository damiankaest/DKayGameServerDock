[CmdletBinding()]
param(
    [string]$DataRoot = '',
    [string]$ServersRoot = '',
    [string]$SteamCmdPath = '',
    [string]$JavaPath = '',
    [switch]$IncludeBuildTools
)

$ErrorActionPreference = 'Stop'

function First-Value([string]$Explicit, [string]$EnvironmentName, [string]$Fallback) {
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit }
    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentName, 'Machine')
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) { return $environmentValue }
    return $Fallback
}

function Test-WritableDirectory([string]$Name, [string]$Path) {
    try {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
        $probe = Join-Path $Path ".dkay-write-test-$([Guid]::NewGuid().ToString('N')).tmp"
        Set-Content -Path $probe -Value 'DKayGameServerDock readiness probe' -NoNewline
        Remove-Item -Path $probe -Force
        return [PSCustomObject]@{ Check = $Name; Ready = $true; Value = $Path; Message = 'Writable' }
    }
    catch {
        return [PSCustomObject]@{ Check = $Name; Ready = $false; Value = $Path; Message = $_.Exception.Message }
    }
}

function Resolve-Executable([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    if (Test-Path -LiteralPath $Value -PathType Leaf) { return (Resolve-Path -LiteralPath $Value).Path }
    $command = Get-Command $Value -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

$resolvedDataRoot = First-Value $DataRoot 'DGS_DATA_ROOT' (Join-Path $env:ProgramData 'DKayGameServerDock')
$resolvedServersRoot = First-Value $ServersRoot 'DGS_SERVERS_ROOT' 'C:\GameServers'
$resolvedSteamCmd = First-Value $SteamCmdPath 'DGS_STEAMCMD_PATH' ''
$resolvedJava = First-Value $JavaPath 'DGS_JAVA_PATH' 'java'

$checks = [System.Collections.Generic.List[object]]::new()
$checks.Add((Test-WritableDirectory 'Application data' $resolvedDataRoot))
$checks.Add((Test-WritableDirectory 'Game servers' $resolvedServersRoot))

$javaExecutable = Resolve-Executable $resolvedJava
$javaValue = $resolvedJava
$javaMessage = 'Optional until you install Minecraft Paper'
if ($javaExecutable) {
    $javaValue = $javaExecutable
    $javaMessage = 'Available'
}
$checks.Add([PSCustomObject]@{
    Check = 'Java (Minecraft Paper)'
    Ready = $null -ne $javaExecutable
    Value = $javaValue
    Message = $javaMessage
})

$steamExecutable = Resolve-Executable $resolvedSteamCmd
$steamValue = $resolvedSteamCmd
$steamMessage = 'Optional until you install Counter-Strike 2'
if ($steamExecutable) {
    $steamValue = $steamExecutable
    $steamMessage = 'Available'
}
$checks.Add([PSCustomObject]@{
    Check = 'SteamCMD (CS2)'
    Ready = $null -ne $steamExecutable
    Value = $steamValue
    Message = $steamMessage
})

if ($IncludeBuildTools) {
    foreach ($tool in @('dotnet', 'npm')) {
        $resolvedTool = Resolve-Executable $tool
        $toolMessage = 'Required to publish on this PC'
        if ($resolvedTool) { $toolMessage = 'Available' }
        $checks.Add([PSCustomObject]@{
            Check = "Build tool: $tool"
            Ready = $null -ne $resolvedTool
            Value = $resolvedTool
            Message = $toolMessage
        })
    }
}

$checks | Format-Table -AutoSize

$coreFailure = $checks | Where-Object { $_.Check -in @('Application data', 'Game servers') -and -not $_.Ready }
if ($coreFailure) {
    throw 'Core host readiness checks failed.'
}

Write-Host 'Core host paths are ready. Missing game runtimes can be configured before installing that game.'

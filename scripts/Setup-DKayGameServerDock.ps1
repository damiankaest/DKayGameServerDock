[CmdletBinding()]
param(
    [string]$PayloadDirectory = '',
    [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-Step([int]$Number, [string]$Title) {
    Write-Host ''
    Write-Host "[$Number/6] $Title" -ForegroundColor Cyan
    Write-Host ('-' * ($Title.Length + 6)) -ForegroundColor DarkGray
}

function Read-Value([string]$Prompt, [string]$Default) {
    $answer = Read-Host "$Prompt [$Default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}

function Read-YesNo([string]$Prompt, [bool]$Default) {
    $hint = if ($Default) { 'Y/n' } else { 'y/N' }
    while ($true) {
        $answer = (Read-Host "$Prompt [$hint]").Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
        if ($answer -in @('y', 'yes', 'j', 'ja')) { return $true }
        if ($answer -in @('n', 'no', 'nein')) { return $false }
        Write-Host 'Please answer Y or N.' -ForegroundColor Yellow
    }
}

function Read-Port([string]$Prompt, [int]$Default) {
    while ($true) {
        $answer = Read-Value $Prompt $Default.ToString()
        $value = 0
        if ([int]::TryParse($answer, [ref]$value) -and $value -ge 1 -and $value -le 65535) {
            return $value
        }
        Write-Host 'Enter a port between 1 and 65535.' -ForegroundColor Yellow
    }
}

function Get-ServiceEnvironment([string]$ServiceName) {
    $result = @{}
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $values = (Get-ItemProperty -Path $key -Name Environment -ErrorAction SilentlyContinue).Environment
    foreach ($entry in @($values)) {
        if ($entry -match '^([^=]+)=(.*)$') {
            $result[$Matches[1]] = $Matches[2]
        }
    }
    return $result
}

function Get-ConfiguredValue([hashtable]$Environment, [string]$Name, [string]$Fallback) {
    if ($Environment.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($Environment[$Name])) {
        return $Environment[$Name]
    }
    return $Fallback
}

function Resolve-Executable([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    if (Test-Path -LiteralPath $Value -PathType Leaf) { return (Resolve-Path -LiteralPath $Value).Path }
    $command = Get-Command $Value -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

function Install-SteamCmd([string]$ExecutablePath) {
    $targetDirectory = Split-Path -Parent $ExecutablePath
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    $temporaryArchive = Join-Path $env:TEMP "dkay-steamcmd-$([Guid]::NewGuid().ToString('N')).zip"
    try {
        Write-Host 'Downloading SteamCMD from Valve...' -ForegroundColor DarkCyan
        Invoke-WebRequest -Uri 'https://client-update.steamstatic.com/installer/steamcmd.zip' -OutFile $temporaryArchive -UseBasicParsing
        Expand-Archive -LiteralPath $temporaryArchive -DestinationPath $targetDirectory -Force
        if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
            throw 'Valve SteamCMD archive did not contain steamcmd.exe.'
        }
        Write-Host "SteamCMD installed at $ExecutablePath" -ForegroundColor Green
    }
    finally {
        Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    }
}

function Find-InstalledJava {
    $commandJava = Resolve-Executable 'java'
    if ($commandJava) { return $commandJava }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Microsoft\jdk-*\bin\java.exe'),
        (Join-Path $env:ProgramFiles 'Eclipse Adoptium\jdk-*\bin\java.exe'),
        (Join-Path $env:ProgramFiles 'Java\jdk-*\bin\java.exe')
    )
    foreach ($pattern in $candidates) {
        $match = Get-Item -Path $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($match) { return $match.FullName }
    }
    return $null
}

function Install-JavaWithWinget {
    $winget = Resolve-Executable 'winget'
    if (-not $winget) { return $null }

    Write-Host 'Installing Microsoft OpenJDK 21 LTS with winget...' -ForegroundColor DarkCyan
    & $winget install --id Microsoft.OpenJDK.21 --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        Write-Host "winget could not install Java (exit code $LASTEXITCODE)." -ForegroundColor Yellow
        return $null
    }
    return Find-InstalledJava
}

function Get-LanAddresses {
    try {
        return @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
            Sort-Object InterfaceMetric |
            Select-Object -ExpandProperty IPAddress -Unique)
    }
    catch { return @() }
}

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'The setup wizard must run on Windows.'
}
if (-not (Test-IsAdministrator)) {
    throw 'Run Setup.cmd or this script as Administrator.'
}

Clear-Host
Write-Host 'DKay Game Server Dock' -ForegroundColor Green
Write-Host 'Windows installation and configuration wizard' -ForegroundColor White
Write-Host ''
Write-Host 'This wizard installs a self-contained Windows service. It does not require .NET or Node.js.' -ForegroundColor DarkGray
Write-Host 'Administrator access remains LAN-only. Public access, when enabled, exposes only the read-only guest portal.' -ForegroundColor DarkGray

$serviceName = 'DKayGameServerDock'
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$existingEnvironment = Get-ServiceEnvironment $serviceName

Write-Step 1 'Installation source and mode'
if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
    $PayloadDirectory = Join-Path $PSScriptRoot 'payload'
}
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $PayloadDirectory 'DKay.GameServerDock.Api.exe') -PathType Leaf)) {
    throw "The release payload is missing at '$PayloadDirectory'. Download and extract the Windows release ZIP, then run Setup.cmd."
}
if ($existingService) {
    Write-Host 'Existing installation detected. The wizard will perform an in-place upgrade/repair.' -ForegroundColor Yellow
}
else {
    Write-Host 'New installation detected.' -ForegroundColor Green
}

$customSetup = Read-YesNo 'Use custom paths and ports?' $false
$defaultInstallDirectory = Get-ConfiguredValue $existingEnvironment 'DGS_INSTALL_DIRECTORY' 'C:\Program Files\DKayGameServerDock'
$defaultDataRoot = Get-ConfiguredValue $existingEnvironment 'DGS_DATA_ROOT' 'C:\ProgramData\DKayGameServerDock'
$defaultServersRoot = Get-ConfiguredValue $existingEnvironment 'DGS_SERVERS_ROOT' 'C:\GameServers'
$defaultAdminPort = [int](Get-ConfiguredValue $existingEnvironment 'DGS_ADMIN_PORT' '5080')
$defaultPublicPort = [int](Get-ConfiguredValue $existingEnvironment 'DGS_PUBLIC_PORTAL_PORT' '5081')

if ($customSetup) {
    $installDirectory = Read-Value 'Application directory' $defaultInstallDirectory
    $dataRoot = Read-Value 'Application data directory' $defaultDataRoot
    $serversRoot = Read-Value 'Game server directory' $defaultServersRoot
    $adminPort = Read-Port 'LAN administrator port' $defaultAdminPort
}
else {
    $installDirectory = $defaultInstallDirectory
    $dataRoot = $defaultDataRoot
    $serversRoot = $defaultServersRoot
    $adminPort = $defaultAdminPort
}

Write-Step 2 'Game runtimes'
$configuredSteamCmd = Get-ConfiguredValue $existingEnvironment 'DGS_STEAMCMD_PATH' 'C:\Tools\SteamCMD\steamcmd.exe'
$steamCmdPath = Resolve-Executable $configuredSteamCmd
$installCs2Support = Read-YesNo 'Enable Counter-Strike 2 support (install SteamCMD if needed)?' $true
if ($installCs2Support -and -not $steamCmdPath) {
    $steamCmdPath = if ($customSetup) { Read-Value 'SteamCMD executable path' $configuredSteamCmd } else { $configuredSteamCmd }
    Install-SteamCmd $steamCmdPath
}
elseif (-not $steamCmdPath) {
    $steamCmdPath = ''
}

$configuredJava = Get-ConfiguredValue $existingEnvironment 'DGS_JAVA_PATH' 'java'
$javaPath = Resolve-Executable $configuredJava
if (-not $javaPath) { $javaPath = Find-InstalledJava }
$installMinecraftSupport = Read-YesNo 'Enable Minecraft Paper support (Java 21 LTS)?' ($null -ne $javaPath)
if ($installMinecraftSupport -and -not $javaPath) {
    if (Read-YesNo 'Java was not found. Install Microsoft OpenJDK 21 with winget?' $true) {
        $javaPath = Install-JavaWithWinget
    }
    if (-not $javaPath) {
        $manualJava = Read-Value 'Path to java.exe (leave as java to configure later)' 'java'
        $javaPath = $manualJava
        Write-Host 'Minecraft readiness may remain yellow until that Java executable is installed.' -ForegroundColor Yellow
    }
}
elseif (-not $javaPath) {
    $javaPath = 'java'
}

Write-Step 3 'LAN and guest access'
$openLanFirewall = Read-YesNo "Allow administrator UI on this LAN (TCP $adminPort, LocalSubnet only)?" $true
$wasPublic = (Get-ConfiguredValue $existingEnvironment 'DGS_PUBLIC_PORTAL_ENABLED' 'false') -eq 'true'
$enablePublicPortal = Read-YesNo 'Enable the read-only guest server list for friends on the internet?' $wasPublic
$publicHost = Get-ConfiguredValue $existingEnvironment 'DGS_PUBLIC_HOST' ''
$publicPortalName = Get-ConfiguredValue $existingEnvironment 'DGS_PUBLIC_PORTAL_NAME' 'DKay Game Servers'
$publicPortalPort = $defaultPublicPort
if ($enablePublicPortal) {
    while ($true) {
        $publicHost = Read-Value 'MyFRITZ/DynDNS host name (without http://)' $publicHost
        $normalizedHost = $publicHost.Trim().Trim('[', ']')
        if ([Uri]::CheckHostName($normalizedHost) -ne [UriHostNameType]::Unknown) { break }
        Write-Host 'Enter a valid DNS host name or public IP address.' -ForegroundColor Yellow
    }
    $publicPortalName = Read-Value 'Guest page title' $publicPortalName
    if ($customSetup) { $publicPortalPort = Read-Port 'Public guest portal port' $publicPortalPort }
    if ($publicPortalPort -eq $adminPort) { throw 'Public and administrator ports must be different.' }
}

Write-Step 4 'Review'
Write-Host "Application:       $installDirectory"
Write-Host "Application data:  $dataRoot"
Write-Host "Game servers:      $serversRoot"
Write-Host "Administrator UI:  http://<server-lan-ip>:$adminPort (LAN firewall: $openLanFirewall)"
Write-Host "SteamCMD:           $(if ($steamCmdPath) { $steamCmdPath } else { 'not configured' })"
Write-Host "Java:               $javaPath"
if ($enablePublicPortal) {
    Write-Host "Guest page:         http://${publicHost}:$publicPortalPort/join" -ForegroundColor Green
    Write-Host "FRITZ!Box rule:     TCP $publicPortalPort -> this PC TCP $publicPortalPort" -ForegroundColor Yellow
}
else {
    Write-Host 'Guest page:         disabled'
}
Write-Host ''
Write-Host "Service identity:   NT AUTHORITY\LocalService" -ForegroundColor DarkGray
Write-Host 'Game ports are opened separately only when you publish a specific game server.' -ForegroundColor DarkGray
if (-not (Read-YesNo 'Apply this configuration now?' $true)) {
    Write-Host 'Setup cancelled. No service changes were made.' -ForegroundColor Yellow
    return
}

Write-Step 5 'Install service and validate'
$serviceInstaller = Join-Path $PSScriptRoot 'install-windows-service.ps1'
if (-not (Test-Path -LiteralPath $serviceInstaller -PathType Leaf)) {
    throw "Service installer is missing at '$serviceInstaller'."
}

$installerParameters = @{
    SourceDirectory = $PayloadDirectory
    InstallDirectory = $installDirectory
    DataRoot = $dataRoot
    ServersRoot = $serversRoot
    SteamCmdPath = $steamCmdPath
    JavaPath = $javaPath
    Port = $adminPort
    OpenLanFirewall = $openLanFirewall
    EnablePublicPortal = $enablePublicPortal
    PublicPortalPort = $publicPortalPort
    PublicHost = $publicHost
    PublicPortalName = $publicPortalName
}
& $serviceInstaller @installerParameters

$lanAddresses = Get-LanAddresses
$localUrl = "http://localhost:$adminPort"
$lanUrl = if ($lanAddresses.Count -gt 0) { "http://$($lanAddresses[0]):$adminPort" } else { "http://<server-lan-ip>:$adminPort" }
$summaryPath = Join-Path $dataRoot 'installation-summary.txt'
$summary = @(
    'DKay Game Server Dock installation summary',
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')",
    '',
    "Local administrator UI: $localUrl",
    "LAN administrator UI:   $lanUrl",
    "Windows service:         $serviceName (NT AUTHORITY\LocalService)",
    "Application:             $installDirectory",
    "Application data:        $dataRoot",
    "Game servers:            $serversRoot",
    "SteamCMD:                $steamCmdPath",
    "Java:                    $javaPath",
    '',
    'NEXT STEPS',
    '1. Open the administrator UI and create the first local administrator.',
    '2. Open Host and verify all readiness checks.',
    '3. Create a game server. Open only that game port in Windows Firewall and FRITZ!Box.',
    '4. Never forward administrator port 5080.'
)
if ($enablePublicPortal) {
    $summary += @(
        '',
        "Guest page:             http://${publicHost}:$publicPortalPort/join",
        "FRITZ!Box portal rule:  TCP $publicPortalPort -> server TCP $publicPortalPort",
        'The guest page lists only servers you explicitly publish in the administrator UI.'
    )
}
$summary | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Step 6 'Ready'
Write-Host 'DKay Game Server Dock is installed and healthy.' -ForegroundColor Green
Write-Host "Open locally: $localUrl"
Write-Host "Open in LAN:  $lanUrl"
Write-Host "Saved guide:  $summaryPath"
if ($enablePublicPortal) {
    Write-Host ''
    Write-Host 'Manual FRITZ!Box step still required:' -ForegroundColor Yellow
    Write-Host "Forward TCP $publicPortalPort to this server. Never forward TCP $adminPort."
    Write-Host 'Each CS2/Minecraft game port is forwarded separately when you publish that server.'
}

if (-not $SkipBrowser) {
    try { Start-Process $localUrl } catch { Write-Host 'Open the URL above in a browser.' -ForegroundColor Yellow }
}

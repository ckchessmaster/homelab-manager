<#
.SYNOPSIS
    Installs and registers the ControlPlane Compute Node Agent as a Windows Service.

.DESCRIPTION
    Downloads the static Go agent binary for Windows, registers the service
    as ControlPlaneAgent with automatic recovery and startup, and starts execution.

.PARAMETER HubUrl
    WebSocket URL for the ControlPlane backend (e.g. ws://192.168.1.159:5000/agent-hub).

.PARAMETER Token
    ControlPlane API key or node authentication token.

.PARAMETER NodeId
    Unique Host GUID or node identifier.

.PARAMETER BinaryUrl
    Optional direct URL to the Windows agent binary. Inferred from HubUrl if omitted.

.PARAMETER InstallDir
    Target directory for binary installation. Defaults to 'C:\Program Files\ControlPlaneAgent'.

.PARAMETER ServiceName
    Windows Service identifier name. Defaults to 'ControlPlaneAgent'.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]$HubUrl,

    [Parameter(Mandatory = $true)]
    [string]$Token,

    [Parameter(Mandatory = $true)]
    [string]$NodeId,

    [Parameter(Mandatory = $false)]
    [string]$BinaryUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$InstallDir = "C:\Program Files\ControlPlaneAgent",

    [Parameter(Mandatory = $false)]
    [string]$ServiceName = "ControlPlaneAgent",

    [Parameter(Mandatory = $false)]
    [switch]$Insecure
)

$ErrorActionPreference = "Stop"

# 1. Require Administrator Elevation
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "ControlPlane Agent installation requires Administrator privileges. Please run from an elevated PowerShell prompt."
    exit 1
}

if ($Insecure) {
    Write-Host "Insecure mode active: Bypassing SSL/TLS certificate verification." -ForegroundColor Yellow
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  ControlPlane Compute Node Agent Windows Setup  " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "Target Node ID:  $NodeId"
Write-Host "Hub URL:         $HubUrl"
Write-Host "Install Dir:     $InstallDir"
Write-Host ""

# 2. Derive BinaryUrl if not specified
if ([string]::IsNullOrWhiteSpace($BinaryUrl)) {
    $httpBase = $HubUrl
    if ($httpBase.StartsWith("wss://", [System.StringComparison]::OrdinalIgnoreCase)) {
        $httpBase = "https://" + $httpBase.Substring(6)
    } elseif ($httpBase.StartsWith("ws://", [System.StringComparison]::OrdinalIgnoreCase)) {
        $httpBase = "http://" + $httpBase.Substring(5)
    }

    $uri = [System.Uri]$httpBase
    $baseHost = "$($uri.Scheme)://$($uri.Authority)"
    $BinaryUrl = "$baseHost/api/v1/agents/binaries/windows-amd64"
}

# 3. Create target directory
if (-not (Test-Path -Path $InstallDir)) {
    Write-Host "Creating installation directory: $InstallDir..."
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$exePath = Join-Path $InstallDir "controlplane-agent.exe"

# 4. Stop existing service if running
$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingSvc) {
    Write-Host "Stopping running service '$ServiceName' for update..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# 5. Download binary
Write-Host "Downloading agent binary from: $BinaryUrl..."
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$tempExe = Join-Path $InstallDir "controlplane-agent.tmp.exe"
try {
    Invoke-WebRequest -Uri $BinaryUrl -OutFile $tempExe -UseBasicParsing
} catch {
    Write-Error "Failed to download agent binary from '$BinaryUrl': $($_.Exception.Message)"
    exit 1
}

if (Test-Path $exePath) {
    $oldExe = Join-Path $InstallDir "controlplane-agent.old.exe"
    Remove-Item $oldExe -Force -ErrorAction SilentlyContinue
    Rename-Item -Path $exePath -NewName "controlplane-agent.old.exe" -Force -ErrorAction SilentlyContinue
}

Move-Item -Path $tempExe -Destination $exePath -Force

# 6. Configure Windows Service
$fullCmdLine = '"{0}" --hub-url "{1}" --token "{2}" --node-id "{3}"' -f $exePath, $HubUrl, $Token, $NodeId
if ($Insecure) {
    $fullCmdLine += " --insecure"
}

if (-not $existingSvc) {
    Write-Host "Registering '$ServiceName' as a Windows Service..."
    New-Service -Name $ServiceName -BinaryPathName ('"{0}"' -f $exePath) -StartupType Automatic -DisplayName "ControlPlane Compute Node Agent" | Out-Null
}

# Ensure exact command-line arguments are stored in registry ImagePath
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name "ImagePath" -Value $fullCmdLine -Type ExpandString -Force

# Set service description and failure restart policy (restart after 5s, 10s, 60s)
& sc.exe description $ServiceName "ControlPlane outbound telemetry and management daemon" | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null

# 7. Start Service
Write-Host "Starting '$ServiceName' service..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$finalStatus = (Get-Service -Name $ServiceName).Status
if ($finalStatus -eq "Running") {
    Write-Host ""
    Write-Host "SUCCESS: ControlPlane Agent is installed and running!" -ForegroundColor Green
    Write-Host "Service: $ServiceName" -ForegroundColor Green
    Write-Host "Outbound telemetry active to $HubUrl" -ForegroundColor Green
} else {
    Write-Warning "Service was started, but reports state '$finalStatus'. Check Event Viewer (Application log) for details."
}

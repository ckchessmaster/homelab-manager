<#
.SYNOPSIS
    Enables Windows portproxy and firewall rules to expose ControlPlane running in WSL2 to local network hosts.

.DESCRIPTION
    Forwards incoming LAN traffic on port 5029 (API / WebSocket agent hub) and port 5173 (React frontend)
    from the Windows host into the WSL2 virtual machine, and creates corresponding Windows Defender
    Firewall inbound allow rules.
#>

[CmdletBinding()]
param(
    [int[]]$Ports = @(5029, 5173),
    [string]$ListenAddress = "0.0.0.0",
    [string]$ConnectAddress = ""
)

# Elevate to Administrator if not currently elevated
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Administrator privileges required. Requesting elevation..." -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    if ($ConnectAddress) {
        $argList += @("-ConnectAddress", "`"$ConnectAddress`"")
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    exit 0
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "   ControlPlane: Enable WSL2 Port Forwarding     " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Resolve target WSL IP address
$targetIp = $ConnectAddress
if (-not $targetIp) {
    try {
        $wslOutput = (wsl.exe -e hostname -I 2>$null)
        if ($wslOutput) {
            $targetIp = ($wslOutput.Trim() -split '\s+')[0]
        }
    }
    catch {
        $targetIp = $null
    }
}

if (-not $targetIp) {
    $targetIp = "127.0.0.1"
    Write-Host "[!] Could not automatically detect WSL2 IP. Defaulting to loopback: $targetIp" -ForegroundColor Yellow
} else {
    Write-Host "[*] Detected WSL2 target IP: $targetIp" -ForegroundColor Green
}

# Configure portproxy and firewall for each port
foreach ($port in $Ports) {
    Write-Host "`n[*] Configuring port $port..." -ForegroundColor Yellow

    # Remove any existing portproxy rule for this port
    netsh interface portproxy delete v4tov4 listenport=$port listenaddress=$ListenAddress 2>$null | Out-Null

    # Add portproxy rule
    netsh interface portproxy add v4tov4 listenport=$port listenaddress=$ListenAddress connectport=$port connectaddress=$targetIp
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [+] Portproxy configured: ${ListenAddress}:${port} -> ${targetIp}:${port}" -ForegroundColor Green
    } else {
        Write-Host "  [-] Failed to configure portproxy for port $port" -ForegroundColor Red
    }

    # Add/Update Firewall Rule
    $ruleName = "ControlPlane Inbound Port $port"
    Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $ruleName `
                        -Direction Inbound `
                        -LocalPort $port `
                        -Protocol TCP `
                        -Action Allow `
                        -Profile Any `
                        -Description "Allows incoming traffic from LAN to ControlPlane services in WSL2" | Out-Null
    Write-Host "  [+] Firewall rule '$ruleName' enabled (TCP $port)" -ForegroundColor Green
}

# Start WSL2 internal bridge (socat) so WSL accepts connections on its external IP and routes to 127.0.0.1
Write-Host "`n[*] Configuring WSL2 internal bridge (socat)..." -ForegroundColor Yellow
try {
    $socatCheck = (wsl.exe -e which socat 2>$null)
    if (-not $socatCheck) {
        Write-Host "  [*] socat not found in WSL. Installing socat via apt..." -ForegroundColor Yellow
        wsl.exe -u root -e bash -c "apt-get update -qq && apt-get install -y -qq socat"
    }

    foreach ($port in $Ports) {
        # Kill any prior socat instances on this port
        wsl.exe -e bash -c "pkill -f 'socat.*LISTEN:$port' 2>/dev/null || true"
        # Start background bridge from targetIp to 127.0.0.1
        wsl.exe -e bash -c "nohup socat TCP-LISTEN:$port,bind=$targetIp,reuseaddr,fork TCP:127.0.0.1:$port >/dev/null 2>&1 &"
        Write-Host "  [+] WSL2 socat bridge active for port $port (${targetIp}:${port} -> 127.0.0.1:${port})" -ForegroundColor Green
    }
} catch {
    Write-Host "  [!] Notice: Could not start socat in WSL: $_" -ForegroundColor Yellow
}

# Determine Windows LAN IP for display
$lanIps = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { ($_.InterfaceAlias -like "*Ethernet*" -or $_.InterfaceAlias -like "*Wi-Fi*") -and $_.IPAddress -notlike "169.254*" -and $_.IPAddress -notlike "172.*" } |
    Select-Object -ExpandProperty IPAddress

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "Active Portproxy Configuration:" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
netsh interface portproxy show all

Write-Host "`n[OK] Portproxy and firewall configured successfully!" -ForegroundColor Green
if ($lanIps) {
    Write-Host "`nYour ControlPlane services are now accessible across your LAN at:" -ForegroundColor Cyan
    foreach ($ip in $lanIps) {
        Write-Host "  - Hub WebSocket URL: ws://${ip}:5029/agent-hub" -ForegroundColor White
        Write-Host "  - Web Dashboard:     http://${ip}:5173" -ForegroundColor White
    }
}
Write-Host ""
pause

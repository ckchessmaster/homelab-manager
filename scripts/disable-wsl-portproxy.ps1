<#
.SYNOPSIS
    Disables Windows portproxy and removes firewall rules created for ControlPlane WSL2 forwarding.

.DESCRIPTION
    Removes portproxy rules for port 5029 (API / WebSocket hub) and 5173 (React frontend)
    and cleans up inbound Windows Firewall rules.
#>

[CmdletBinding()]
param(
    [int[]]$Ports = @(5029, 5173),
    [string]$ListenAddress = "0.0.0.0"
)

# Elevate to Administrator if not currently elevated
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Administrator privileges required. Requesting elevation..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    exit 0
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "   ControlPlane: Disable WSL2 Port Forwarding    " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

foreach ($port in $Ports) {
    Write-Host "`n[*] Cleaning up port $port..." -ForegroundColor Yellow

    # Delete portproxy rule
    netsh interface portproxy delete v4tov4 listenport=$port listenaddress=$ListenAddress 2>$null | Out-Null
    Write-Host "  [-] Removed portproxy rule for ${ListenAddress}:${port}" -ForegroundColor Green

    # Remove firewall rules (including generic matches)
    $ruleName = "ControlPlane Inbound Port $port"
    Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    Write-Host "  [-] Removed firewall rule '$ruleName'" -ForegroundColor Green
}

# Clean up any legacy or generic ControlPlane firewall rules
Get-NetFirewallRule -DisplayName "*ControlPlane*" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-NetFirewallRule -Name $_.Name -ErrorAction SilentlyContinue
    Write-Host "  [-] Removed rule '$($_.DisplayName)'" -ForegroundColor Green
}

# Stop WSL2 internal socat bridges
Write-Host "`n[*] Terminating WSL2 internal bridges (socat)..." -ForegroundColor Yellow
try {
    foreach ($port in $Ports) {
        wsl.exe -e bash -c "pkill -f 'socat.*LISTEN:$port' 2>/dev/null || true"
    }
    Write-Host "  [-] WSL2 socat processes terminated" -ForegroundColor Green
} catch {
    Write-Host "  [!] Notice: Could not clean up socat in WSL: $_" -ForegroundColor Yellow
}

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "Remaining Portproxy Rules:" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
netsh interface portproxy show all

Write-Host "`n[OK] Portproxy rules and firewall entries cleaned up successfully." -ForegroundColor Green
Write-Host ""
pause

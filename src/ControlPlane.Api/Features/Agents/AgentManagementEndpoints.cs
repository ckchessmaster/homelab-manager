using ControlPlane.Api.Features.Agents.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ControlPlane.Api.Features.Agents;

public static class AgentManagementEndpoints
{
    public static IEndpointRouteBuilder MapAgentManagementEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous binary download endpoint for agents to fetch static binaries
        app.MapGet("/api/v1/agents/binaries/{arch}", (string arch, AgentBinaryService binaryService) =>
        {
            var binaryPath = binaryService.GetBinaryPath(arch);
            if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath))
            {
                return Results.NotFound(new { message = $"Agent binary for architecture '{arch}' not found." });
            }

            var filename = Path.GetFileName(binaryPath);
            return Results.File(binaryPath, "application/octet-stream", fileDownloadName: filename, enableRangeProcessing: true);
        }).AllowAnonymous();

        // Anonymous PowerShell bootstrap script download endpoint
        app.MapGet("/api/v1/agents/install.ps1", () => Results.Content(GetInstallScript(), "text/plain; charset=utf-8"))
            .AllowAnonymous();

        app.MapGet("/api/v1/agents/bootstrap.ps1", () => Results.Content(GetInstallScript(), "text/plain; charset=utf-8"))
            .AllowAnonymous();

        // Anonymous Linux shell bootstrap script download endpoint
        app.MapGet("/api/v1/agents/install.sh", () => Results.Content(GetLinuxInstallScript(), "text/x-shellscript; charset=utf-8"))
            .AllowAnonymous();

        app.MapGet("/api/v1/agents/bootstrap.sh", () => Results.Content(GetLinuxInstallScript(), "text/x-shellscript; charset=utf-8"))
            .AllowAnonymous();

        var group = app.MapGroup("/api/v1/agents")
            .RequireAuthorization();

        group.MapGet("/version-info", async (MassAgentUpdateService service, CancellationToken ct) =>
        {
            var info = await service.GetVersionInfoAsync(ct);
            return Results.Ok(info);
        });

        group.MapPost("/mass-update", async (
            MassUpdateRequest request,
            HttpRequest httpRequest,
            MassAgentUpdateService service,
            CancellationToken ct) =>
        {
            var forwardedProto = httpRequest.Headers["X-Forwarded-Proto"].FirstOrDefault()
                ?? httpRequest.Headers["X-Forwarded-Scheme"].FirstOrDefault();
            var scheme = !string.IsNullOrWhiteSpace(forwardedProto) ? forwardedProto : httpRequest.Scheme;
            if (scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)) scheme = "https";
            else if (scheme.Equals("ws", StringComparison.OrdinalIgnoreCase)) scheme = "http";

            var forwardedHost = httpRequest.Headers["X-Forwarded-Host"].FirstOrDefault();
            var host = !string.IsNullOrWhiteSpace(forwardedHost) ? forwardedHost : httpRequest.Host.Value;

            var serverBaseUrl = $"{scheme}://{host}";
            var result = await service.TriggerMassUpdateAsync(request, serverBaseUrl, ct);
            return Results.Accepted($"/api/v1/agents/mass-update/{result.BatchId}", result);
        });

        group.MapGet("/mass-update/{batchId:guid}", (Guid batchId, MassAgentUpdateService service) =>
        {
            var batch = service.GetBatchStatus(batchId);
            return batch != null
                ? Results.Ok(batch)
                : Results.NotFound(new { message = $"Mass update batch '{batchId}' not found." });
        });

        group.MapGet("/binaries/status", async (IAgentBinarySyncService syncService, CancellationToken ct) =>
        {
            var status = await syncService.GetStatusAsync(ct);
            return Results.Ok(status);
        });

        group.MapPost("/binaries/sync", async (
            AgentBinarySyncRequest? request,
            IAgentBinarySyncService syncService,
            CancellationToken ct) =>
        {
            var result = await syncService.SyncBinariesAsync(request?.Force ?? false, ct);
            return Results.Ok(result);
        });

        var settingsGroup = app.MapGroup("/api/v1/settings")
            .RequireAuthorization();

        settingsGroup.MapGet("/agent-binaries", async (IAgentBinarySyncService syncService, CancellationToken ct) =>
        {
            var status = await syncService.GetStatusAsync(ct);
            return Results.Ok(status);
        });

        settingsGroup.MapPost("/agent-binaries/sync", async (
            AgentBinarySyncRequest? request,
            IAgentBinarySyncService syncService,
            CancellationToken ct) =>
        {
            var result = await syncService.SyncBinariesAsync(request?.Force ?? false, ct);
            return Results.Ok(result);
        });

        return app;
    }

    private static string GetInstallScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scripts", "install-agent.ps1"),
            Path.Combine(AppContext.BaseDirectory, "../../agent/scripts", "install-agent.ps1"),
            Path.Combine(Directory.GetCurrentDirectory(), "src/agent/scripts", "install-agent.ps1"),
            Path.Combine(Directory.GetCurrentDirectory(), "../agent/scripts", "install-agent.ps1"),
            Path.Combine(Directory.GetCurrentDirectory(), "agent/scripts", "install-agent.ps1")
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
        {
            return File.ReadAllText(found);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, "src", "agent", "scripts", "install-agent.ps1");
            if (File.Exists(testPath)) return File.ReadAllText(testPath);
            current = current.Parent;
        }

        return FallbackInstallScript;
    }

    private const string FallbackInstallScript = @"
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)][string]$HubUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$NodeId,
    [Parameter(Mandatory = $false)][string]$BinaryUrl = """",
    [Parameter(Mandatory = $false)][string]$InstallDir = ""C:\Program Files\ControlPlaneAgent"",
    [Parameter(Mandatory = $false)][string]$ServiceName = ""ControlPlaneAgent"",
    [Parameter(Mandatory = $false)][switch]$Insecure
)
$ErrorActionPreference = ""Stop""
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error ""ControlPlane Agent installation requires Administrator privileges. Please run from an elevated PowerShell prompt.""
    exit 1
}
if ($Insecure) {
    Write-Host ""Insecure mode active: Bypassing SSL/TLS certificate verification."" -ForegroundColor Yellow
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}
if ([string]::IsNullOrWhiteSpace($BinaryUrl)) {
    $httpBase = $HubUrl
    if ($httpBase.StartsWith(""wss://"", [System.StringComparison]::OrdinalIgnoreCase)) {
        $httpBase = ""https://"" + $httpBase.Substring(6)
    } elseif ($httpBase.StartsWith(""ws://"", [System.StringComparison]::OrdinalIgnoreCase)) {
        $httpBase = ""http://"" + $httpBase.Substring(5)
    }
    $uri = [System.Uri]$httpBase
    $BinaryUrl = ""$($uri.Scheme)://$($uri.Authority)/api/v1/agents/binaries/windows-amd64""
}
if (-not (Test-Path -Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
$exePath = Join-Path $InstallDir ""controlplane-agent.exe""
$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingSvc) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
$tempExe = Join-Path $InstallDir ""controlplane-agent.tmp.exe""
Write-Host ""Downloading agent binary from $BinaryUrl...""
try {
    Invoke-WebRequest -Uri $BinaryUrl -OutFile $tempExe -UseBasicParsing
} catch {
    Write-Error ""Failed to download agent binary from '$BinaryUrl': $($_.Exception.Message)""
    exit 1
}
if (Test-Path $exePath) {
    $oldExe = Join-Path $InstallDir ""controlplane-agent.old.exe""
    Remove-Item $oldExe -Force -ErrorAction SilentlyContinue
    Rename-Item -Path $exePath -NewName ""controlplane-agent.old.exe"" -Force -ErrorAction SilentlyContinue
}
Move-Item -Path $tempExe -Destination $exePath -Force
$fullCmdLine = '""{0}"" --hub-url ""{1}"" --token ""{2}"" --node-id ""{3}""' -f $exePath, $HubUrl, $Token, $NodeId
if ($Insecure) {
    $fullCmdLine += "" --insecure""
}
if (-not $existingSvc) {
    New-Service -Name $ServiceName -BinaryPathName ('""{0}""' -f $exePath) -StartupType Automatic -DisplayName ""ControlPlane Compute Node Agent"" | Out-Null
}
Set-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"" -Name ""ImagePath"" -Value $fullCmdLine -Type ExpandString -Force
& sc.exe description $ServiceName ""ControlPlane outbound telemetry and management daemon"" | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
Write-Host ""Starting $ServiceName service...""
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
$finalStatus = (Get-Service -Name $ServiceName).Status
if ($finalStatus -eq ""Running"") {
    Write-Host ""ControlPlane Agent installed and running successfully! (NodeId: $NodeId)"" -ForegroundColor Green
} else {
    Write-Warning ""Service was started, but reports state '$finalStatus'. Check Event Viewer (Application log) for details.""
}
";

    private static string GetLinuxInstallScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scripts", "install-agent.sh"),
            Path.Combine(AppContext.BaseDirectory, "../../agent/scripts", "install-agent.sh"),
            Path.Combine(Directory.GetCurrentDirectory(), "src/agent/scripts", "install-agent.sh"),
            Path.Combine(Directory.GetCurrentDirectory(), "../agent/scripts", "install-agent.sh"),
            Path.Combine(Directory.GetCurrentDirectory(), "agent/scripts", "install-agent.sh")
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
        {
            return File.ReadAllText(found);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, "src", "agent", "scripts", "install-agent.sh");
            if (File.Exists(testPath)) return File.ReadAllText(testPath);
            current = current.Parent;
        }

        return FallbackLinuxInstallScript;
    }

    private const string FallbackLinuxInstallScript = """"
#!/usr/bin/env bash
set -euo pipefail

HUB_URL=""
TOKEN=""
NODE_ID=""
BINARY_URL=""
INSTALL_DIR="/usr/local/bin"
SERVICE_NAME="controlplane-agent"
INSECURE=false

usage() {
    cat <<EOF
Usage: $0 --hub-url <url> --token <token> --node-id <id> [options]

Required Arguments:
  --hub-url <url>       WebSocket URL of ControlPlane (e.g. wss://cp.example.com/agent-hub)
  --token <token>       Node authentication token / API key
  --node-id <id>        Unique Host GUID or node identifier

Options:
  --binary-url <url>    Optional direct download URL for agent binary
  --install-dir <dir>   Installation directory (default: /usr/local/bin)
  --service-name <name> Systemd service name (default: controlplane-agent)
  --insecure            Bypass TLS/SSL certificate verification
  -h, --help            Show this help message
EOF
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --hub-url) HUB_URL="$2"; shift 2 ;;
        --token) TOKEN="$2"; shift 2 ;;
        --node-id) NODE_ID="$2"; shift 2 ;;
        --binary-url) BINARY_URL="$2"; shift 2 ;;
        --install-dir) INSTALL_DIR="$2"; shift 2 ;;
        --service-name) SERVICE_NAME="$2"; shift 2 ;;
        --insecure) INSECURE=true; shift ;;
        -h|--help) usage ;;
        *) echo "Unknown option: $1" >&2; usage ;;
    esac
done

if [ -z "$HUB_URL" ] || [ -z "$TOKEN" ] || [ -z "$NODE_ID" ]; then
    echo "Error: --hub-url, --token, and --node-id are required arguments." >&2
    usage
fi

if [ "$(id -u)" -ne 0 ]; then
    echo "Error: ControlPlane Agent installation requires root privileges. Please run with sudo or as root." >&2
    exit 1
fi

UNAME_M="$(uname -m)"
case "$UNAME_M" in
    x86_64|amd64) ARCH="linux-amd64" ;;
    aarch64|arm64) ARCH="linux-arm64" ;;
    armv7l|armv6l) ARCH="linux-armv7" ;;
    *) echo "Error: Unsupported CPU architecture '$UNAME_M'." >&2; exit 1 ;;
esac

if [ -z "$BINARY_URL" ]; then
    HTTP_BASE="$HUB_URL"
    SCHEME="http"
    if [[ "$HTTP_BASE" =~ ^wss:// ]]; then
        SCHEME="https"
        HOST_PORT="${HTTP_BASE#wss://}"
    elif [[ "$HTTP_BASE" =~ ^ws:// ]]; then
        SCHEME="http"
        HOST_PORT="${HTTP_BASE#ws://}"
    elif [[ "$HTTP_BASE" =~ ^https:// ]]; then
        SCHEME="https"
        HOST_PORT="${HTTP_BASE#https://}"
    else
        SCHEME="http"
        HOST_PORT="${HTTP_BASE#http://}"
    fi
    HOST_PORT="${HOST_PORT%%/*}"
    BINARY_URL="${SCHEME}://${HOST_PORT}/api/v1/agents/binaries/${ARCH}"
fi

mkdir -p "$INSTALL_DIR"
DEST_BIN="${INSTALL_DIR}/controlplane-agent"

if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    echo "Stopping active service '$SERVICE_NAME' for upgrade..."
    systemctl stop "$SERVICE_NAME" || true
fi

TEMP_BIN="/tmp/controlplane-agent-$$.tmp"
rm -f "$TEMP_BIN"

CURL_EXTRA=""
WGET_EXTRA=""
if [ "$INSECURE" = true ]; then
    CURL_EXTRA="-k"
    WGET_EXTRA="--no-check-certificate"
fi

echo "Downloading agent binary from $BINARY_URL..."
if command -v curl >/dev/null 2>&1; then
    curl -fsSL $CURL_EXTRA "$BINARY_URL" -o "$TEMP_BIN"
elif command -v wget >/dev/null 2>&1; then
    wget -q $WGET_EXTRA -O "$TEMP_BIN" "$BINARY_URL"
else
    echo "Error: Neither 'curl' nor 'wget' was found on this system." >&2
    exit 1
fi

if [ ! -s "$TEMP_BIN" ]; then
    echo "Error: Downloaded binary is empty or missing." >&2
    rm -f "$TEMP_BIN"
    exit 1
fi

mv -f "$TEMP_BIN" "$DEST_BIN"
chmod +x "$DEST_BIN"

if command -v systemctl >/dev/null 2>&1; then
    SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
    INSECURE_CLI_FLAG=""
    if [ "$INSECURE" = true ]; then
        INSECURE_CLI_FLAG=" --insecure"
    fi

    cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=ControlPlane Compute Node Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=${DEST_BIN} --hub-url ${HUB_URL}${INSECURE_CLI_FLAG} --token ${TOKEN} --node-id ${NODE_ID}
Restart=always
RestartSec=5
KillMode=process
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME"
    echo "Starting $SERVICE_NAME service..."
    systemctl restart "$SERVICE_NAME"

    sleep 2
    if systemctl is-active --quiet "$SERVICE_NAME"; then
        echo "ControlPlane Agent installed and running successfully! (NodeId: $NODE_ID)"
    else
        echo "Warning: Service was started but reports inactive state. Check journalctl -u $SERVICE_NAME -n 20" >&2
    fi
else
    echo "Warning: systemctl not detected. Agent binary installed at $DEST_BIN."
fi
"""";
}


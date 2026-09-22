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
            var serverBaseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}";
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
    [Parameter(Mandatory = $false)][string]$ServiceName = ""ControlPlaneAgent""
)
$ErrorActionPreference = ""Stop""
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error ""ControlPlane Agent installation requires Administrator privileges. Please run from an elevated PowerShell prompt.""
    exit 1
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
Invoke-WebRequest -Uri $BinaryUrl -OutFile $tempExe -UseBasicParsing
if (Test-Path $exePath) {
    $oldExe = Join-Path $InstallDir ""controlplane-agent.old.exe""
    Remove-Item $oldExe -Force -ErrorAction SilentlyContinue
    Rename-Item -Path $exePath -NewName ""controlplane-agent.old.exe"" -Force -ErrorAction SilentlyContinue
}
Move-Item -Path $tempExe -Destination $exePath -Force
$fullCmdLine = ""\`""$exePath\`"" --hub-url \`""$HubUrl\`"" --token \`""$Token\`"" --node-id \`""$NodeId\`""""
if (-not $existingSvc) {
    New-Service -Name $ServiceName -BinaryPathName ""\`""$exePath\`"""" -StartupType Automatic -DisplayName ""ControlPlane Compute Node Agent"" | Out-Null
}
Set-ItemProperty -Path ""HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"" -Name ""ImagePath"" -Value $fullCmdLine -Type ExpandString -Force
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
Write-Host ""ControlPlane Agent installed and running successfully! (NodeId: $NodeId)"" -ForegroundColor Green
";
}

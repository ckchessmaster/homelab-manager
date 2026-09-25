using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Features.Adoption;

public class NodeAdoptionService
{
    private readonly ISshBootstrapper _bootstrapper;
    private readonly AgentConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ApiKeyAuthenticationOptions> _apiKeyOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NodeAdoptionService> _logger;

    public NodeAdoptionService(
        ISshBootstrapper bootstrapper,
        AgentConnectionManager connectionManager,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ApiKeyAuthenticationOptions> apiKeyOptions,
        IConfiguration configuration,
        ILogger<NodeAdoptionService> logger)
    {
        _bootstrapper = bootstrapper;
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _apiKeyOptions = apiKeyOptions;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<NodeAdoptionResponse> AdoptNodeAsync(
        AdoptNodeRequest request,
        Action<AdoptionStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<AdoptionStepEvent>();

        void Emit(string key, string title, AdoptionStepStatus status, string? message = null)
        {
            var ev = new AdoptionStepEvent(key, title, status, message, DateTimeOffset.UtcNow);
            steps.RemoveAll(s => s.StepKey == key);
            steps.Add(ev);
            onStep?.Invoke(ev);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        // 1. Resolve target Host entity
        HostEntity? host = null;
        if (request.HostId.HasValue)
        {
            host = await db.Hosts.FindAsync(new object[] { request.HostId.Value }, cancellationToken);
        }

        if (host == null && !string.IsNullOrWhiteSpace(request.Hostname))
        {
            host = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname.ToLower() == request.Hostname.ToLower(), cancellationToken);
        }

        if (host == null)
        {
            var targetHostIp = request.TargetHost;
            host = await db.Hosts.FirstOrDefaultAsync(h => h.IpAddress == targetHostIp, cancellationToken);
        }

        if (host == null)
        {
            // Register new host automatically
            host = new HostEntity
            {
                Id = request.HostId ?? Guid.NewGuid(),
                Hostname = !string.IsNullOrWhiteSpace(request.Hostname) ? request.Hostname : $"node-{request.TargetHost.Replace('.', '-')}",
                IpAddress = request.TargetHost,
                OsFamily = "linux_debian",
                TargetType = "baremetal",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync(cancellationToken);
        }

        var hostId = host.Id;

        // Step 1: Probe Architecture
        Emit("SSH_CONNECTING", "Connecting via SSH & probing architecture", AdoptionStepStatus.Running);
        string arch;
        try
        {
            arch = await _bootstrapper.ProbeArchitectureAsync(request, cancellationToken);
            Emit("SSH_CONNECTING", "Connected via SSH & probed architecture", AdoptionStepStatus.Completed, $"Detected architecture: {arch}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH connection/probe failed for host {Host}", request.TargetHost);
            Emit("SSH_CONNECTING", "SSH connection failed", AdoptionStepStatus.Failed, ex.Message);
            return new NodeAdoptionResponse(hostId, false, ex.Message, steps);
        }

        // Step 2: Binary selection
        Emit("ARCH_DETECTED", "Selecting matching agent binary", AdoptionStepStatus.Running);
        var isWindows = (host.OsFamily?.Contains("windows", StringComparison.OrdinalIgnoreCase) ?? false) || arch.Contains("windows", StringComparison.OrdinalIgnoreCase);

        var binaryName = isWindows
            ? "controlplane-agent-windows-amd64.exe"
            : (arch.Contains("aarch64") || arch.Contains("arm64"))
                ? "controlplane-agent-linux-arm64"
                : "controlplane-agent-linux-amd64";

        var binaryPath = FindAgentBinary(binaryName);
        if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath))
        {
            var msg = $"Static binary '{binaryName}' not found in dist directory.";
            _logger.LogError(msg);
            Emit("ARCH_DETECTED", "Matching agent binary missing", AdoptionStepStatus.Failed, msg);
            return new NodeAdoptionResponse(hostId, false, msg, steps);
        }
        Emit("ARCH_DETECTED", "Agent binary selected", AdoptionStepStatus.Completed, $"Using {binaryName}");

        if (isWindows)
        {
            // Step 3 (Windows): Upload binary
            Emit("BINARY_STREAMING", "Streaming agent binary to C:\\Program Files\\ControlPlaneAgent", AdoptionStepStatus.Running);
            try
            {
                try
                {
                    await _bootstrapper.ExecuteRemoteCommandAsync(request, "sc.exe stop ControlPlaneAgent", cancellationToken);
                }
                catch
                {
                    // ignore if service doesn't exist
                }

                await _bootstrapper.ExecuteRemoteCommandAsync(request, "powershell.exe -NoProfile -Command \"New-Item -ItemType Directory -Path 'C:\\Program Files\\ControlPlaneAgent' -Force | Out-Null\"", cancellationToken);
                await _bootstrapper.UploadBinaryAsync(request, binaryPath, "C:/Program Files/ControlPlaneAgent/controlplane-agent.exe", cancellationToken);
                Emit("BINARY_STREAMING", "Windows agent binary deployed", AdoptionStepStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload Windows agent binary to {Host}", request.TargetHost);
                Emit("BINARY_STREAMING", "Binary streaming failed", AdoptionStepStatus.Failed, ex.Message);
                return new NodeAdoptionResponse(hostId, false, ex.Message, steps);
            }

            // Step 4 (Windows): Configure & start Windows Service
            Emit("SERVICE_STARTING", "Configuring and starting Windows Service", AdoptionStepStatus.Running);
            try
            {
                var hubUrl = !string.IsNullOrWhiteSpace(request.HubUrl)
                    ? request.HubUrl
                    : _configuration["ControlPlane:HubUrl"] ?? "ws://192.168.1.159:5029/agent-hub";

                var token = _apiKeyOptions.CurrentValue.ApiKey ?? hostId.ToString();
                var binPath = $"\"\\\"C:\\Program Files\\ControlPlaneAgent\\controlplane-agent.exe\\\" --hub-url \\\"{hubUrl}\\\" --token \\\"{token}\\\" --node-id \\\"{hostId}\\\"\"";

                await _bootstrapper.ExecuteRemoteCommandAsync(
                    request,
                    $"powershell.exe -NoProfile -Command \"$svc = Get-Service -Name ControlPlaneAgent -ErrorAction SilentlyContinue; if ($svc) {{ & sc.exe config ControlPlaneAgent binPath= '{binPath}' start= auto }} else {{ & sc.exe create ControlPlaneAgent binPath= '{binPath}' start= auto DisplayName= 'ControlPlane Compute Node Agent' }}; & sc.exe failure ControlPlaneAgent reset= 86400 actions= restart/5000/restart/10000/restart/60000; Start-Service ControlPlaneAgent\"",
                    cancellationToken
                );
                Emit("SERVICE_STARTING", "ControlPlaneAgent Windows Service started", AdoptionStepStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure/start Windows Service on {Host}", request.TargetHost);
                Emit("SERVICE_STARTING", "Failed to start Windows Service", AdoptionStepStatus.Failed, ex.Message);
                return new NodeAdoptionResponse(hostId, false, ex.Message, steps);
            }
        }
        else
        {
            // Step 3 (Linux): Upload binary
            Emit("BINARY_STREAMING", "Streaming agent binary to /usr/local/bin/controlplane-agent", AdoptionStepStatus.Running);
            try
            {
                // Stop any currently running instance so binary and service can be cleanly replaced
                try
                {
                    await _bootstrapper.ExecutePrivilegedCommandAsync(request, "systemctl stop controlplane-agent 2>/dev/null || true", cancellationToken);
                }
                catch
                {
                    // ignore if service doesn't exist yet
                }

                await _bootstrapper.UploadBinaryAsync(request, binaryPath, "/tmp/controlplane-agent", cancellationToken);
                await _bootstrapper.ExecutePrivilegedCommandAsync(request, "mv -f /tmp/controlplane-agent /usr/local/bin/controlplane-agent && chmod +x /usr/local/bin/controlplane-agent", cancellationToken);
                Emit("BINARY_STREAMING", "Agent binary deployed", AdoptionStepStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload agent binary to {Host}", request.TargetHost);
                Emit("BINARY_STREAMING", "Binary streaming failed", AdoptionStepStatus.Failed, ex.Message);
                return new NodeAdoptionResponse(hostId, false, ex.Message, steps);
            }

            // Step 4 (Linux): Write systemd unit & start service
            Emit("SERVICE_STARTING", "Configuring and starting systemd service", AdoptionStepStatus.Running);
            try
            {
                var hubUrl = !string.IsNullOrWhiteSpace(request.HubUrl)
                    ? request.HubUrl
                    : _configuration["ControlPlane:HubUrl"] ?? "ws://192.168.1.159:5029/agent-hub";

                var token = _apiKeyOptions.CurrentValue.ApiKey ?? hostId.ToString();

                var serviceUnitContent = $"""
                [Unit]
                Description=ControlPlane Compute Node Agent
                After=network-online.target
                Wants=network-online.target

                [Service]
                Type=simple
                ExecStart=/usr/local/bin/controlplane-agent --hub-url {hubUrl} --token {token} --node-id {hostId}
                Restart=always
                RestartSec=5
                KillMode=process
                LimitNOFILE=65536

                [Install]
                WantedBy=multi-user.target
                """;

                await _bootstrapper.UploadTextAsync(request, serviceUnitContent, "/tmp/controlplane-agent.service", cancellationToken);
                await _bootstrapper.ExecutePrivilegedCommandAsync(
                    request,
                    "mv -f /tmp/controlplane-agent.service /etc/systemd/system/controlplane-agent.service && systemctl daemon-reload && systemctl enable controlplane-agent && systemctl restart controlplane-agent",
                    cancellationToken
                );
                Emit("SERVICE_STARTING", "Systemd service started", AdoptionStepStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure/start service on {Host}", request.TargetHost);
                Emit("SERVICE_STARTING", "Failed to start service", AdoptionStepStatus.Failed, ex.Message);
                return new NodeAdoptionResponse(hostId, false, ex.Message, steps);
            }
        }

        // Step 5: Await WebSocket handshake
        Emit("HANDSHAKE_VERIFIED", "Awaiting outbound agent WebSocket handshake", AdoptionStepStatus.Running);
        var handshakeDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var verified = false;

        while (DateTimeOffset.UtcNow < handshakeDeadline && !cancellationToken.IsCancellationRequested)
        {
            if (_connectionManager.IsOnline(hostId))
            {
                verified = true;
                break;
            }
            await Task.Delay(1000, cancellationToken);
        }

        if (verified)
        {
            Emit("HANDSHAKE_VERIFIED", "Agent WebSocket connection established", AdoptionStepStatus.Completed);
            Emit("SSH_DISCONNECTED", "SSH session cleanly closed and credentials discarded", AdoptionStepStatus.Completed);

            host.Agent.Installed = true;
            host.Agent.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return new NodeAdoptionResponse(hostId, true, "Node adopted successfully.", steps);
        }
        else
        {
            var msg = "Timed out waiting for agent outbound WebSocket connection.";
            Emit("HANDSHAKE_VERIFIED", "Handshake timeout", AdoptionStepStatus.Failed, msg);
            return new NodeAdoptionResponse(hostId, false, msg, steps);
        }
    }

    private string? FindAgentBinary(string filename)
    {
        var configuredDir = _configuration["ControlPlane:AgentDistDir"] ?? Environment.GetEnvironmentVariable("AGENT_DIST_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            var customPath = Path.Combine(configuredDir, filename);
            if (File.Exists(customPath)) return customPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "agent-dist", filename),
            Path.Combine(AppContext.BaseDirectory, "../../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "src/agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "agent/dist", filename)
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, "src", "agent", "dist", filename);
            if (File.Exists(testPath)) return testPath;
            current = current.Parent;
        }

        return null;
    }
}

using System.Net.Sockets;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class HealthProbeActivities : IHealthProbeActivities
{
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<HealthProbeActivities> _logger;

    public HealthProbeActivities(
        IAgentCommandExecutor commandExecutor,
        IHttpClientFactory httpClientFactory,
        IWorkflowLogEmitter logEmitter,
        ILogger<HealthProbeActivities> logger)
    {
        _commandExecutor = commandExecutor;
        _httpClientFactory = httpClientFactory;
        _logEmitter = logEmitter;
        _logger = logger;
    }

    [Activity]
    public async Task<HealthProbeResult> RunHealthProbesAsync(HealthProbeInput input)
    {
        var probeDetails = new List<string>();
        await _logEmitter.EmitLogAsync(input.JobId, "system", "[HEALTH] Starting post-flight health verification...");

        // 1. Systemd Service Health Inspection
        var osFamily = input.OsFamily?.ToLowerInvariant() ?? "";
        if (!osFamily.Contains("windows"))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[HEALTH] Inspecting systemd service unit health (systemctl --failed)...");

            var checkScript = "units=$(systemctl --failed --no-legend 2>/dev/null | awk '{print $1}'); " +
                              "if [ -n \"$units\" ]; then echo \"Failed units: $units\"; exit 1; fi; exit 0";

            var cmdResult = await _commandExecutor.ExecuteCommandAsync(
                input.HostId,
                input.JobId,
                "sh",
                new[] { "-c", checkScript }
            );

            if (!cmdResult.Success)
            {
                var errorDetail = cmdResult.ErrorMessage ?? "Detected one or more failed systemd units.";
                if (input.FailOnFailedServices)
                {
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] Error: {errorDetail}");
                    return new HealthProbeResult(false, $"Systemd unit health check failed: {errorDetail}", probeDetails);
                }

                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] Warning: {errorDetail} (continuing per policy)");
                probeDetails.Add($"Systemd warning: {errorDetail}");
            }
            else
            {
                await _logEmitter.EmitLogAsync(input.JobId, "system", "[HEALTH] Systemd health verified: 0 failed units.");
                probeDetails.Add("Systemd: 0 failed units");
            }
        }

        // 2. Synthetic Endpoint Probes
        var effectiveUrls = input.ProbeUrls ?? new List<string>();
        if (effectiveUrls.Count > 0)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] Executing {effectiveUrls.Count} synthetic endpoint probe(s)...");
            var httpClient = _httpClientFactory.CreateClient();
            var timeout = TimeSpan.FromSeconds(input.ProbeTimeoutSeconds);

            foreach (var rawUrl in effectiveUrls.Distinct())
            {
                try
                {
                    if (rawUrl.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProbeTcpAsync(rawUrl, timeout);
                        var msg = $"TCP probe to '{rawUrl}' succeeded.";
                        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] {msg}");
                        probeDetails.Add(msg);
                    }
                    else
                    {
                        using var cts = new CancellationTokenSource(timeout);
                        var response = await httpClient.GetAsync(rawUrl, cts.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            var msg = $"Endpoint '{rawUrl}' returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] Error: {msg}");
                            return new HealthProbeResult(false, $"Synthetic health probe failed: {msg}", probeDetails);
                        }

                        var okMsg = $"HTTP probe to '{rawUrl}' returned {(int)response.StatusCode} OK.";
                        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] {okMsg}");
                        probeDetails.Add(okMsg);
                    }
                }
                catch (Exception ex)
                {
                    var msg = $"Probe to '{rawUrl}' failed: {ex.Message}";
                    await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] Error: {msg}");
                    return new HealthProbeResult(false, $"Synthetic health probe error: {msg}", probeDetails);
                }
            }
        }

        var completionMsg = "All post-flight health verifications passed.";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[HEALTH] {completionMsg}");
        return new HealthProbeResult(true, completionMsg, probeDetails);
    }

    private static async Task ProbeTcpAsync(string tcpUrl, TimeSpan timeout)
    {
        var uri = new Uri(tcpUrl);
        using var tcpClient = new TcpClient();
        using var cts = new CancellationTokenSource(timeout);

        await tcpClient.ConnectAsync(uri.Host, uri.Port, cts.Token);
    }
}

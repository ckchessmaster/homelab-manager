using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Executes post-flight health probes: verifies systemd unit statuses and probes configured service endpoints.
/// </summary>
public class PostFlightHealthProbeStep : IJobStep
{
    private readonly IReadOnlyList<string> _probeUrls;
    private readonly bool _failOnFailedServices;
    private readonly TimeSpan _probeTimeout;
    private readonly IHttpClientFactory? _httpClientFactory;

    public string StepName => "Post-Flight Health Probes";

    public PostFlightHealthProbeStep(
        IEnumerable<string>? probeUrls = null,
        bool failOnFailedServices = true,
        TimeSpan? probeTimeout = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _probeUrls = probeUrls?.ToList() ?? new List<string>();
        _failOnFailedServices = failOnFailedServices;
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(10);
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        await context.EmitLogAsync("system", "[HEALTH] Starting post-flight health verification...", ct);

        // 1. Systemd Service Health Inspection
        var osFamily = context.TargetHost.OsFamily?.ToLowerInvariant() ?? "";
        if (!osFamily.Contains("windows"))
        {
            await context.EmitLogAsync("system", "[HEALTH] Inspecting systemd service unit health (systemctl --failed)...", ct);

            var checkScript = "units=$(systemctl --failed --no-legend 2>/dev/null | awk '{print $1}'); " +
                              "if [ -n \"$units\" ]; then echo \"Failed units: $units\"; exit 1; fi; exit 0";

            var cmdResult = await context.CommandExecutor.ExecuteCommandAsync(
                context.HostId,
                context.JobId,
                "sh",
                new[] { "-c", checkScript },
                ct
            );

            if (!cmdResult.Success)
            {
                var errorDetail = cmdResult.ErrorMessage ?? "Detected one or more failed systemd units.";
                if (_failOnFailedServices)
                {
                    await context.EmitLogAsync("system", $"[HEALTH] Error: {errorDetail}", ct);
                    return JobStepResult.Failed($"Systemd unit health check failed: {errorDetail}");
                }

                await context.EmitLogAsync("system", $"[HEALTH] Warning: {errorDetail} (continuing per policy)", ct);
            }
            else
            {
                await context.EmitLogAsync("system", "[HEALTH] Systemd health verified: 0 failed units.", ct);
            }
        }

        // 2. Synthetic Endpoint Probes
        var effectiveUrls = new List<string>(_probeUrls);
        if (context.State.TryGetValue("ProbeUrls", out var ctxUrls) && ctxUrls is IEnumerable<string> urlEnum)
        {
            effectiveUrls.AddRange(urlEnum);
        }

        if (effectiveUrls.Count > 0)
        {
            await context.EmitLogAsync("system", $"[HEALTH] Executing {effectiveUrls.Count} synthetic endpoint probe(s)...", ct);

            var clientFactory = _httpClientFactory ?? ResolveHttpClientFactory(context);
            var httpClient = clientFactory?.CreateClient() ?? new HttpClient();

            foreach (var rawUrl in effectiveUrls.Distinct())
            {
                try
                {
                    if (rawUrl.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProbeTcpAsync(rawUrl, _probeTimeout, ct);
                        await context.EmitLogAsync("system", $"[HEALTH] TCP probe to '{rawUrl}' succeeded.", ct);
                    }
                    else
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(_probeTimeout);

                        var response = await httpClient.GetAsync(rawUrl, cts.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            var msg = $"Endpoint '{rawUrl}' returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                            await context.EmitLogAsync("system", $"[HEALTH] Error: {msg}", ct);
                            return JobStepResult.Failed($"Synthetic health probe failed: {msg}");
                        }

                        await context.EmitLogAsync("system", $"[HEALTH] HTTP probe to '{rawUrl}' returned {(int)response.StatusCode} OK.", ct);
                    }
                }
                catch (Exception ex)
                {
                    var msg = $"Probe to '{rawUrl}' failed: {ex.Message}";
                    await context.EmitLogAsync("system", $"[HEALTH] Error: {msg}", ct);
                    return JobStepResult.Failed($"Synthetic health probe error: {msg}", ex);
                }
            }
        }

        await context.EmitLogAsync("system", "[HEALTH] All post-flight health verifications passed.", ct);
        return JobStepResult.Succeeded("Post-flight health verification succeeded.", targetState: UpdateJobState.Completed);
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    private static async Task ProbeTcpAsync(string tcpUrl, TimeSpan timeout, CancellationToken ct)
    {
        var uri = new Uri(tcpUrl);
        using var tcpClient = new TcpClient();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        await tcpClient.ConnectAsync(uri.Host, uri.Port, linkedCts.Token);
    }

    private static IHttpClientFactory? ResolveHttpClientFactory(JobExecutionContext context)
    {
        using var scope = context.ScopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IHttpClientFactory>();
    }
}

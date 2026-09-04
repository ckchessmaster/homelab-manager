using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Handles polling and completion tracking for asynchronous Proxmox tasks.
/// </summary>
public class ProxmoxTaskPoller
{
    private readonly ILogger<ProxmoxTaskPoller> _logger;

    public ProxmoxTaskPoller(ILogger<ProxmoxTaskPoller> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Polls the task status repeatedly until it reaches 'stopped', times out, or cancellation is requested.
    /// </summary>
    public async Task<ProxmoxTaskStatus> PollUntilStoppedAsync(
        string node,
        string upid,
        Func<string, string, CancellationToken, Task<ProxmoxTaskStatus>> getStatusFunc,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Starting poll for Proxmox task {Upid} on node {Node} (timeout: {Timeout}s)", upid, node, timeout.TotalSeconds);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var status = await getStatusFunc(node, upid, cts.Token);
                if (status.IsStopped)
                {
                    if (!status.IsSuccess)
                    {
                        var errorMsg = $"Proxmox task '{upid}' failed with exit status: {status.ExitStatus ?? "unknown error"}";
                        _logger.LogError(errorMsg);
                        throw new InvalidOperationException(errorMsg);
                    }

                    _logger.LogDebug("Proxmox task {Upid} completed successfully (exitstatus: {ExitStatus})", upid, status.ExitStatus ?? "OK");
                    return status;
                }

                await Task.Delay(pollInterval, cts.Token);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Task polling canceled by caller for task {Upid}", upid);
            throw;
        }
        catch (OperationCanceledException)
        {
            var timeoutMsg = $"Proxmox task '{upid}' on node '{node}' timed out after {timeout.TotalSeconds} seconds.";
            _logger.LogError(timeoutMsg);
            throw new TimeoutException(timeoutMsg);
        }

        throw new TimeoutException($"Proxmox task '{upid}' on node '{node}' timed out after {timeout.TotalSeconds} seconds.");
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// REST API client for interacting with Proxmox VE hypervisors.
/// </summary>
public class ProxmoxClient : IProxmoxClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxmoxOptions _options;
    private readonly ProxmoxTaskPoller _poller;
    private readonly ILogger<ProxmoxClient> _logger;

    public ProxmoxClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ProxmoxOptions> options,
        ProxmoxTaskPoller poller,
        ILogger<ProxmoxClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _poller = poller;
        _logger = logger;
    }

    public async Task<string> CreateVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        string? description = null,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        ValidateConfiguration();

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot";

        _logger.LogInformation("Requesting snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        var body = new ProxmoxSnapshotRequest(snapName, description);
        using var request = CreateRequest(HttpMethod.Post, endpoint, JsonContent.Create(body));
        using var response = await SendAsync(request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<string> RollbackVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        ValidateConfiguration();

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot/{Uri.EscapeDataString(snapName)}/rollback";

        _logger.LogInformation("Requesting rollback to snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        using var request = CreateRequest(HttpMethod.Post, endpoint);
        using var response = await SendAsync(request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<string> DeleteVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        ValidateConfiguration();

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot/{Uri.EscapeDataString(snapName)}";

        _logger.LogInformation("Requesting deletion of snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        using var request = CreateRequest(HttpMethod.Delete, endpoint);
        using var response = await SendAsync(request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<ProxmoxTaskStatus> GetTaskStatusAsync(
        string node,
        string upid,
        CancellationToken ct = default)
    {
        ValidateConfiguration();

        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/tasks/{Uri.EscapeDataString(upid)}/status";

        using var request = CreateRequest(HttpMethod.Get, endpoint);
        using var response = await SendAsync(request, ct);

        var statusWrapper = await response.Content.ReadFromJsonAsync<ProxmoxTaskStatusResponse>(ct);
        if (statusWrapper?.Data == null)
        {
            throw new InvalidOperationException($"Failed to deserialize task status response for UPID {upid}");
        }

        return statusWrapper.Data;
    }

    public async Task<ProxmoxTaskStatus> PollTaskCompletionAsync(
        string node,
        string upid,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(_options.TaskPollTimeoutSeconds > 0 ? _options.TaskPollTimeoutSeconds : 300);
        var pollInterval = TimeSpan.FromMilliseconds(_options.TaskPollIntervalMilliseconds > 0 ? _options.TaskPollIntervalMilliseconds : 1000);

        return await _poller.PollUntilStoppedAsync(
            node,
            upid,
            GetTaskStatusAsync,
            effectiveTimeout,
            pollInterval,
            ct
        );
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Proxmox BaseUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiTokenId) || string.IsNullOrWhiteSpace(_options.ApiTokenSecret))
        {
            throw new InvalidOperationException("Proxmox ApiTokenId and ApiTokenSecret are not configured.");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var root = baseUrl.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/api2/json";

        var uri = $"{root}{path}";
        var request = new HttpRequestMessage(method, uri);

        if (content != null)
        {
            request.Content = content;
        }

        var tokenHeaderValue = _options.ApiTokenId.StartsWith("PVEAPIToken=", StringComparison.OrdinalIgnoreCase)
            ? $"{_options.ApiTokenId}={_options.ApiTokenSecret}"
            : $"PVEAPIToken={_options.ApiTokenId}={_options.ApiTokenSecret}";

        request.Headers.TryAddWithoutValidation("Authorization", tokenHeaderValue);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clientName = _options.AllowSelfSignedCert
            ? ProxmoxProbeService.InsecureHttpClientName
            : ProxmoxProbeService.StandardHttpClientName;

        var client = _httpClientFactory.CreateClient(clientName);
        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Proxmox request to {Uri} failed with status {StatusCode}: {Body}", request.RequestUri, response.StatusCode, body);
            throw new HttpRequestException($"Proxmox API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
        }

        return response;
    }

    private static async Task<string> ExtractTaskUpidAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var result = await response.Content.ReadFromJsonAsync<ProxmoxTaskResponse>(ct);
        if (string.IsNullOrWhiteSpace(result?.Data))
        {
            throw new InvalidOperationException("Proxmox response did not contain a valid task UPID.");
        }

        return result.Data;
    }
}

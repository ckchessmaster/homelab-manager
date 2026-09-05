using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// REST API client for interacting with Proxmox VE hypervisors.
/// </summary>
public class ProxmoxClient : IProxmoxClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxmoxOptions _fallbackOptions;
    private readonly ProxmoxTaskPoller _poller;
    private readonly ILogger<ProxmoxClient> _logger;
    private readonly IAdapterConfigService? _configService;

    public ProxmoxClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ProxmoxOptions> options,
        ProxmoxTaskPoller poller,
        ILogger<ProxmoxClient> logger,
        IAdapterConfigService? configService = null)
    {
        _httpClientFactory = httpClientFactory;
        _fallbackOptions = options.Value;
        _poller = poller;
        _logger = logger;
        _configService = configService;
    }

    private async Task<ProxmoxOptions> GetOptionsAsync(CancellationToken ct)
    {
        if (_configService != null)
        {
            return await _configService.GetActiveProxmoxOptionsAsync(ct);
        }
        return _fallbackOptions;
    }

    public async Task<string> CreateVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        string? description = null,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot";

        _logger.LogInformation("Requesting snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        var body = new ProxmoxSnapshotRequest(snapName, description);
        using var request = CreateRequest(options, HttpMethod.Post, endpoint, JsonContent.Create(body));
        using var response = await SendAsync(options, request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<string> RollbackVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot/{Uri.EscapeDataString(snapName)}/rollback";

        _logger.LogInformation("Requesting rollback to snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        using var request = CreateRequest(options, HttpMethod.Post, endpoint);
        using var response = await SendAsync(options, request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<string> DeleteVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot/{Uri.EscapeDataString(snapName)}";

        _logger.LogInformation("Requesting deletion of snapshot '{SnapName}' on node '{Node}' for {VmType} {Vmid}", snapName, node, vmType, vmid);

        using var request = CreateRequest(options, HttpMethod.Delete, endpoint);
        using var response = await SendAsync(options, request, ct);

        return await ExtractTaskUpidAsync(response, ct);
    }

    public async Task<ProxmoxTaskStatus> GetTaskStatusAsync(
        string node,
        string upid,
        CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);

        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/tasks/{Uri.EscapeDataString(upid)}/status";

        using var request = CreateRequest(options, HttpMethod.Get, endpoint);
        using var response = await SendAsync(options, request, ct);

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
        var options = await GetOptionsAsync(ct);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(options.TaskPollTimeoutSeconds > 0 ? options.TaskPollTimeoutSeconds : 300);
        var pollInterval = TimeSpan.FromMilliseconds(options.TaskPollIntervalMilliseconds > 0 ? options.TaskPollIntervalMilliseconds : 1000);

        return await _poller.PollUntilStoppedAsync(
            node,
            upid,
            GetTaskStatusAsync,
            effectiveTimeout,
            pollInterval,
            ct
        );
    }

    public async Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);
        var resources = new List<ProxmoxClusterResourceDto>();

        // 1. Try cluster-wide resource discovery: /cluster/resources?type=vm and fallback to /cluster/resources
        var clusterEndpoints = new[] { "/cluster/resources?type=vm", "/cluster/resources" };
        foreach (var endpoint in clusterEndpoints)
        {
            try
            {
                using var request = CreateRequest(options, HttpMethod.Get, endpoint);
                using var response = await SendAsync(options, request, ct);
                var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
                if (doc != null && doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataArray.EnumerateArray())
                    {
                        try
                        {
                            var type = GetSafeString(item, "type")?.ToLowerInvariant() ?? "";
                            if (type != "qemu" && type != "lxc") continue;

                            var vmid = GetSafeInt(item, "vmid");
                            if (vmid <= 0) continue;

                            var node = GetSafeString(item, "node") ?? "";
                            var id = GetSafeString(item, "id") ?? $"{type}/{vmid}";
                            var name = GetSafeString(item, "name");
                            var status = GetSafeString(item, "status") ?? "unknown";
                            var mem = GetSafeLong(item, "mem");
                            var maxmem = GetSafeLong(item, "maxmem");
                            var disk = GetSafeLong(item, "disk");
                            var maxdisk = GetSafeLong(item, "maxdisk");
                            var uptime = GetSafeLong(item, "uptime");
                            var tags = GetSafeString(item, "tags");

                            if (!resources.Any(r => r.Vmid == vmid && string.Equals(r.Node, node, StringComparison.OrdinalIgnoreCase)))
                            {
                                resources.Add(new ProxmoxClusterResourceDto(
                                    Id: id,
                                    Node: node,
                                    Type: type,
                                    Vmid: vmid,
                                    Name: name,
                                    Status: status,
                                    MaxMem: maxmem,
                                    Mem: mem,
                                    MaxDisk: maxdisk,
                                    Disk: disk,
                                    Uptime: uptime,
                                    Tags: tags
                                ));
                            }
                        }
                        catch (Exception itemEx)
                        {
                            _logger.LogWarning(itemEx, "Failed parsing resource item from {Endpoint}", endpoint);
                        }
                    }
                }

                if (resources.Count > 0)
                {
                    _logger.LogInformation("Discovered {Count} VMs/LXCs from cluster endpoint {Endpoint}", resources.Count, endpoint);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Querying {Endpoint} failed or was not permitted. Trying next fallback.", endpoint);
            }
        }

        // 2. If cluster resources returned 0 items, fallback to per-node enumeration
        if (resources.Count == 0)
        {
            _logger.LogInformation("Cluster endpoint returned 0 VMs. Enumerating directly via /nodes API...");
            var nodes = await ListNodesAsync(ct);

            foreach (var nodeDto in nodes)
            {
                var nodeName = nodeDto.Node;
                if (string.IsNullOrWhiteSpace(nodeName)) continue;

                // Query QEMU VMs for this node: GET /nodes/{node}/qemu
                try
                {
                    using var qemuReq = CreateRequest(options, HttpMethod.Get, $"/nodes/{Uri.EscapeDataString(nodeName)}/qemu");
                    using var qemuResp = await SendAsync(options, qemuReq, ct);
                    var qemuDoc = await qemuResp.Content.ReadFromJsonAsync<JsonDocument>(ct);
                    if (qemuDoc != null && qemuDoc.RootElement.TryGetProperty("data", out var qemuArray) && qemuArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var vm in qemuArray.EnumerateArray())
                        {
                            try
                            {
                                var vmid = GetSafeInt(vm, "vmid");
                                if (vmid <= 0) continue;

                                var vmName = GetSafeString(vm, "name");
                                var vmStatus = GetSafeString(vm, "status") ?? "unknown";
                                var mem = GetSafeLong(vm, "mem");
                                var maxmem = GetSafeLong(vm, "maxmem");
                                var disk = GetSafeLong(vm, "disk");
                                var maxdisk = GetSafeLong(vm, "maxdisk");
                                var uptime = GetSafeLong(vm, "uptime");
                                var tags = GetSafeString(vm, "tags");

                                if (!resources.Any(r => r.Vmid == vmid && string.Equals(r.Node, nodeName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    resources.Add(new ProxmoxClusterResourceDto(
                                        Id: $"qemu/{vmid}",
                                        Node: nodeName,
                                        Type: "qemu",
                                        Vmid: vmid,
                                        Name: vmName,
                                        Status: vmStatus,
                                        MaxMem: maxmem,
                                        Mem: mem,
                                        MaxDisk: maxdisk,
                                        Disk: disk,
                                        Uptime: uptime,
                                        Tags: tags
                                    ));
                                }
                            }
                            catch (Exception vmEx)
                            {
                                _logger.LogWarning(vmEx, "Failed to parse QEMU VM entry on node {Node}", nodeName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query QEMU VMs on node {Node}", nodeName);
                }

                // Query LXC containers for this node: GET /nodes/{node}/lxc
                try
                {
                    using var lxcReq = CreateRequest(options, HttpMethod.Get, $"/nodes/{Uri.EscapeDataString(nodeName)}/lxc");
                    using var lxcResp = await SendAsync(options, lxcReq, ct);
                    var lxcDoc = await lxcResp.Content.ReadFromJsonAsync<JsonDocument>(ct);
                    if (lxcDoc != null && lxcDoc.RootElement.TryGetProperty("data", out var lxcArray) && lxcArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ctElem in lxcArray.EnumerateArray())
                        {
                            try
                            {
                                var vmid = GetSafeInt(ctElem, "vmid");
                                if (vmid <= 0) continue;

                                var ctName = GetSafeString(ctElem, "name");
                                var ctStatus = GetSafeString(ctElem, "status") ?? "unknown";
                                var mem = GetSafeLong(ctElem, "mem");
                                var maxmem = GetSafeLong(ctElem, "maxmem");
                                var disk = GetSafeLong(ctElem, "disk");
                                var maxdisk = GetSafeLong(ctElem, "maxdisk");
                                var uptime = GetSafeLong(ctElem, "uptime");
                                var tags = GetSafeString(ctElem, "tags");

                                if (!resources.Any(r => r.Vmid == vmid && string.Equals(r.Node, nodeName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    resources.Add(new ProxmoxClusterResourceDto(
                                        Id: $"lxc/{vmid}",
                                        Node: nodeName,
                                        Type: "lxc",
                                        Vmid: vmid,
                                        Name: ctName,
                                        Status: ctStatus,
                                        MaxMem: maxmem,
                                        Mem: mem,
                                        MaxDisk: maxdisk,
                                        Disk: disk,
                                        Uptime: uptime,
                                        Tags: tags
                                    ));
                                }
                            }
                            catch (Exception ctEx)
                            {
                                _logger.LogWarning(ctEx, "Failed to parse LXC container entry on node {Node}", nodeName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query LXC containers on node {Node}", nodeName);
                }
            }
        }

        _logger.LogInformation("DiscoverClusterResourcesAsync returning {Count} total resources", resources.Count);
        return resources;
    }

    public async Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);
        using var request = CreateRequest(options, HttpMethod.Get, "/nodes");
        using var response = await SendAsync(options, request, ct);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
        var nodes = new List<ProxmoxNodeDto>();
        if (doc != null && doc.RootElement.TryGetProperty("data", out var nodesArray) && nodesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in nodesArray.EnumerateArray())
            {
                var nodeName = GetSafeString(elem, "node") ?? "";
                var status = GetSafeString(elem, "status") ?? "unknown";
                double? cpu = elem.TryGetProperty("cpu", out var cpuProp) && cpuProp.ValueKind == JsonValueKind.Number ? cpuProp.GetDouble() : null;
                long? maxCpu = GetSafeLong(elem, "maxcpu");
                long? mem = GetSafeLong(elem, "mem");
                long? maxMem = GetSafeLong(elem, "maxmem");
                long? uptime = GetSafeLong(elem, "uptime");
                nodes.Add(new ProxmoxNodeDto(nodeName, status, cpu, maxCpu, mem, maxMem, uptime));
            }
        }
        return nodes;
    }

    private static int GetSafeInt(JsonElement elem, string propName, int fallback = 0)
    {
        if (elem.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt32(out var val)) return val;
                if (prop.TryGetDouble(out var dVal)) return (int)dVal;
            }
            else if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static long GetSafeLong(JsonElement elem, string propName, long fallback = 0)
    {
        if (elem.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt64(out var val)) return val;
                if (prop.TryGetDouble(out var dVal)) return (long)dVal;
            }
            else if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static string? GetSafeString(JsonElement elem, string propName)
    {
        if (elem.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.ToString();
            }
        }
        return null;
    }

    public async Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);
        if (isLxc)
        {
            try
            {
                var lxcEndpoint = $"/nodes/{Uri.EscapeDataString(node)}/lxc/{vmid}/interfaces";
                using var req = CreateRequest(options, HttpMethod.Get, lxcEndpoint);
                using var resp = await SendAsync(options, req, ct);
                var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
                if (doc != null && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var iface in data.EnumerateArray())
                    {
                        if (iface.TryGetProperty("inet", out var inet) && inet.ValueKind == JsonValueKind.String)
                        {
                            var cidr = inet.GetString();
                            if (!string.IsNullOrWhiteSpace(cidr))
                            {
                                var ip = cidr.Split('/')[0];
                                if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return ip;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch LXC interfaces for {Node}/{Vmid}", node, vmid);
            }
            return null;
        }

        try
        {
            var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/qemu/{vmid}/agent/network-get-interfaces";
            using var req = CreateRequest(options, HttpMethod.Get, endpoint);
            using var resp = await SendAsync(options, req, ct);
            var result = await resp.Content.ReadFromJsonAsync<ProxmoxAgentNetworkResponse>(ct);
            var ifaces = result?.Data?.Result;
            if (ifaces != null)
            {
                foreach (var iface in ifaces)
                {
                    if (iface.Name.Equals("lo", StringComparison.OrdinalIgnoreCase)) continue;
                    if (iface.IpAddresses == null) continue;
                    foreach (var addr in iface.IpAddresses)
                    {
                        if (string.Equals(addr.IpAddressType, "ipv4", StringComparison.OrdinalIgnoreCase))
                        {
                            var ip = addr.IpAddress;
                            if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return ip;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Guest agent network query not available for QEMU VM {Node}/{Vmid}", node, vmid);
        }

        return null;
    }

    public async Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(
        string node,
        int vmid,
        bool isLxc = false,
        CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        ValidateConfiguration(options);

        var vmType = isLxc ? "lxc" : "qemu";
        var endpoint = $"/nodes/{Uri.EscapeDataString(node)}/{vmType}/{vmid}/snapshot";

        _logger.LogDebug("Querying snapshots on node '{Node}' for {VmType} {Vmid}", node, vmType, vmid);

        using var request = CreateRequest(options, HttpMethod.Get, endpoint);
        using var response = await SendAsync(options, request, ct);

        var listResponse = await response.Content.ReadFromJsonAsync<ProxmoxSnapshotListResponse>(ct);
        if (listResponse?.Data == null)
        {
            return new List<ProxmoxSnapshotItem>();
        }

        // Filter out Proxmox internal 'current' state pointer
        return listResponse.Data
            .Where(s => !string.Equals(s.Name, "current", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default)
    {
        try
        {
            var options = await GetOptionsAsync(ct);
            ValidateConfiguration(options);
            using var request = CreateRequest(options, HttpMethod.Get, "/access/permissions");
            using var response = await SendAsync(options, request, ct);
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (doc != null && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var pathProp in data.EnumerateObject())
                {
                    if (pathProp.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var permProp in pathProp.Value.EnumerateObject())
                        {
                            var permName = permProp.Name;
                            if (permName.Contains("VM.Audit", StringComparison.OrdinalIgnoreCase) ||
                                permName.Contains("VM.Allocate", StringComparison.OrdinalIgnoreCase) ||
                                permName.Contains("PVEVMAdmin", StringComparison.OrdinalIgnoreCase) ||
                                permName.Contains("Administrator", StringComparison.OrdinalIgnoreCase) ||
                                permName.Contains("Sys.Audit", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Proxmox token permissions via /access/permissions");
        }

        return false;
    }

    private static void ValidateConfiguration(ProxmoxOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("Proxmox BaseUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiTokenId) || string.IsNullOrWhiteSpace(options.ApiTokenSecret))
        {
            throw new InvalidOperationException("Proxmox ApiTokenId and ApiTokenSecret are not configured.");
        }
    }

    private static HttpRequestMessage CreateRequest(ProxmoxOptions options, HttpMethod method, string path, HttpContent? content = null)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var root = baseUrl.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/api2/json";

        var uri = $"{root}{path}";
        var request = new HttpRequestMessage(method, uri);

        if (content != null)
        {
            request.Content = content;
        }

        var tokenHeaderValue = options.ApiTokenId.StartsWith("PVEAPIToken=", StringComparison.OrdinalIgnoreCase)
            ? $"{options.ApiTokenId}={options.ApiTokenSecret}"
            : $"PVEAPIToken={options.ApiTokenId}={options.ApiTokenSecret}";

        request.Headers.TryAddWithoutValidation("Authorization", tokenHeaderValue);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(ProxmoxOptions options, HttpRequestMessage request, CancellationToken ct)
    {
        var clientName = options.AllowSelfSignedCert
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

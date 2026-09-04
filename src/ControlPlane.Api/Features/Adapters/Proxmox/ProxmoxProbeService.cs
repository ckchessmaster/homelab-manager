using System.Net.Http.Headers;
using System.Text.Json;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

public class ProxmoxProbeService
{
    public const string InsecureHttpClientName = "ProxmoxInsecureClient";
    public const string StandardHttpClientName = "ProxmoxStandardClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxmoxProbeService> _logger;

    public ProxmoxProbeService(IHttpClientFactory httpClientFactory, ILogger<ProxmoxProbeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ProxmoxProbeResponse> ProbeAsync(ProxmoxProbeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            return new ProxmoxProbeResponse(false, ErrorMessage: "BaseUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiTokenId) || string.IsNullOrWhiteSpace(request.ApiTokenSecret))
        {
            return new ProxmoxProbeResponse(false, ErrorMessage: "ApiTokenId and ApiTokenSecret are required.");
        }

        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return new ProxmoxProbeResponse(false, ErrorMessage: "BaseUrl must be a valid HTTP or HTTPS URI.");
        }

        var clientName = request.AllowSelfSignedCert ? InsecureHttpClientName : StandardHttpClientName;
        var client = _httpClientFactory.CreateClient(clientName);
        client.Timeout = TimeSpan.FromSeconds(10);

        // Normalize base URL (strip trailing slash)
        var normalizedBaseUrl = request.BaseUrl.TrimEnd('/');

        // Proxmox API token authorization header format: PVEAPIToken=USER@REALM!TOKENID=SECRET
        var tokenHeaderValue = request.ApiTokenId.StartsWith("PVEAPIToken=", StringComparison.OrdinalIgnoreCase)
            ? $"{request.ApiTokenId}={request.ApiTokenSecret}"
            : $"PVEAPIToken={request.ApiTokenId}={request.ApiTokenSecret}";

        try
        {
            // 1. Query Proxmox Version
            var versionUri = $"{normalizedBaseUrl}/api2/json/version";
            using var versionReq = new HttpRequestMessage(HttpMethod.Get, versionUri);
            versionReq.Headers.TryAddWithoutValidation("Authorization", tokenHeaderValue);

            using var versionResp = await client.SendAsync(versionReq, cancellationToken);
            if (!versionResp.IsSuccessStatusCode)
            {
                var body = await versionResp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Proxmox version check failed with status {StatusCode}: {Body}", versionResp.StatusCode, body);
                return new ProxmoxProbeResponse(
                    false,
                    ErrorMessage: $"Proxmox API returned HTTP {(int)versionResp.StatusCode} ({versionResp.ReasonPhrase}): {body}");
            }

            var versionJson = await versionResp.Content.ReadAsStringAsync(cancellationToken);
            using var versionDoc = JsonDocument.Parse(versionJson);
            var versionData = versionDoc.RootElement.GetProperty("data");

            var version = versionData.TryGetProperty("version", out var vProp) ? vProp.GetString() : null;
            var release = versionData.TryGetProperty("release", out var rProp) ? rProp.GetString() : null;
            var repoid = versionData.TryGetProperty("repoid", out var repoProp) ? repoProp.GetString() : null;

            // 2. Query Nodes list
            var nodes = new List<ProxmoxNodeDto>();
            var nodesUri = $"{normalizedBaseUrl}/api2/json/nodes";
            using var nodesReq = new HttpRequestMessage(HttpMethod.Get, nodesUri);
            nodesReq.Headers.TryAddWithoutValidation("Authorization", tokenHeaderValue);

            using var nodesResp = await client.SendAsync(nodesReq, cancellationToken);
            if (nodesResp.IsSuccessStatusCode)
            {
                var nodesJson = await nodesResp.Content.ReadAsStringAsync(cancellationToken);
                using var nodesDoc = JsonDocument.Parse(nodesJson);
                if (nodesDoc.RootElement.TryGetProperty("data", out var nodesArray) && nodesArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in nodesArray.EnumerateArray())
                    {
                        var nodeName = elem.TryGetProperty("node", out var nProp) ? nProp.GetString() ?? "" : "";
                        var status = elem.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "unknown" : "unknown";

                        double? cpu = elem.TryGetProperty("cpu", out var cpuProp) ? cpuProp.GetDouble() : null;
                        long? maxCpu = elem.TryGetProperty("maxcpu", out var maxCpuProp) ? maxCpuProp.GetInt64() : null;
                        long? mem = elem.TryGetProperty("mem", out var memProp) ? memProp.GetInt64() : null;
                        long? maxMem = elem.TryGetProperty("maxmem", out var maxMemProp) ? maxMemProp.GetInt64() : null;
                        long? uptime = elem.TryGetProperty("uptime", out var upProp) ? upProp.GetInt64() : null;

                        nodes.Add(new ProxmoxNodeDto(nodeName, status, cpu, maxCpu, mem, maxMem, uptime));
                    }
                }
            }

            return new ProxmoxProbeResponse(
                Success: true,
                Version: version,
                Release: release,
                Repoid: repoid,
                Nodes: nodes
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Proxmox at {BaseUrl}", request.BaseUrl);
            return new ProxmoxProbeResponse(false, ErrorMessage: $"Connection failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Connection to Proxmox at {BaseUrl} timed out", request.BaseUrl);
            return new ProxmoxProbeResponse(false, ErrorMessage: "Connection timed out connecting to Proxmox VE API.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error probing Proxmox at {BaseUrl}", request.BaseUrl);
            return new ProxmoxProbeResponse(false, ErrorMessage: $"Unexpected error: {ex.Message}");
        }
    }
}

using ControlPlane.Api.Features.Adapters.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

public class ProxmoxClientFactory : IProxmoxClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ProxmoxOptions> _defaultOptions;
    private readonly ProxmoxTaskPoller _poller;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAdapterConfigService? _configService;

    public ProxmoxClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptions<ProxmoxOptions> defaultOptions,
        ProxmoxTaskPoller poller,
        ILoggerFactory loggerFactory,
        IAdapterConfigService? configService = null)
    {
        _httpClientFactory = httpClientFactory;
        _defaultOptions = defaultOptions;
        _poller = poller;
        _loggerFactory = loggerFactory;
        _configService = configService;
    }

    public async Task<IProxmoxClient> GetClientAsync(string? instanceId = null, CancellationToken ct = default)
    {
        var logger = _loggerFactory.CreateLogger<ProxmoxClient>();
        ProxmoxOptions options;
        if (_configService != null)
        {
            options = await _configService.GetActiveProxmoxOptionsAsync(instanceId, ct);
        }
        else
        {
            options = _defaultOptions.Value;
        }

        return new ProxmoxClient(_httpClientFactory, _defaultOptions, _poller, logger, _configService, instanceId, options);
    }

    public IProxmoxClient CreateClient(ProxmoxOptions options)
    {
        var logger = _loggerFactory.CreateLogger<ProxmoxClient>();
        return new ProxmoxClient(_httpClientFactory, _defaultOptions, _poller, logger, _configService, null, options);
    }
}

using System.Text;
using ControlPlane.Api.Features.Adapters.Config;
using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public class KubernetesClientFactory : IKubernetesClientFactory
{
    private readonly IAdapterConfigService _configService;
    private readonly IOptions<KubernetesConfigOptions> _defaultOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<KubernetesClientFactory> _logger;

    public KubernetesClientFactory(
        IAdapterConfigService configService,
        IOptions<KubernetesConfigOptions> defaultOptions,
        ILoggerFactory loggerFactory,
        ILogger<KubernetesClientFactory> logger)
    {
        _configService = configService;
        _defaultOptions = defaultOptions;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            if (rawCluster != null)
            {
                return BuildClientFromStoredCluster(rawCluster);
            }
            _logger.LogWarning("Cluster configuration '{ClusterId}' not found; falling back to default.", clusterId);
        }

        // Try getting first available cluster
        var all = await _configService.GetKubernetesClustersAsync(ct);
        if (all.Count > 0)
        {
            var firstRaw = await _configService.GetRawKubernetesClusterAsync(all[0].Id, ct);
            if (firstRaw != null)
            {
                return BuildClientFromStoredCluster(firstRaw);
            }
        }

        return BuildDefaultClient();
    }

    public async Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(clusterId, ct);
        var adapterLogger = _loggerFactory.CreateLogger<KubernetesAdapter>();
        return new KubernetesAdapter(client, adapterLogger);
    }

    private IKubernetes BuildClientFromStoredCluster(KubernetesStoredCluster cluster)
    {
        KubernetesClientConfiguration? config = null;

        if (!string.IsNullOrWhiteSpace(cluster.EncryptedKubeConfig))
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(cluster.EncryptedKubeConfig));
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile(
                    stream,
                    currentContext: string.IsNullOrWhiteSpace(cluster.ContextName) ? null : cluster.ContextName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse kubeconfig YAML for cluster '{ClusterName}' ({ClusterId})", cluster.Name, cluster.Id);
            }
        }

        if (config == null && !string.IsNullOrWhiteSpace(cluster.ApiServerUrl))
        {
            config = new KubernetesClientConfiguration
            {
                Host = cluster.ApiServerUrl,
                AccessToken = cluster.EncryptedToken
            };
        }

        config ??= new KubernetesClientConfiguration { Host = "http://localhost:8080" };

        if (cluster.SkipTlsVerify)
        {
            config.SkipTlsVerify = true;
        }

        var client = new k8s.Kubernetes(config);
        client.HttpClient.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private IKubernetes BuildDefaultClient()
    {
        var opts = _defaultOptions.Value;
        KubernetesClientConfiguration config;

        if (opts.InClusterConfig || KubernetesClientConfiguration.IsInCluster())
        {
            try
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InClusterConfig failed; falling back to default config.");
                config = BuildFallbackConfig(opts);
            }
        }
        else if (!string.IsNullOrWhiteSpace(opts.KubeConfigPath) && File.Exists(opts.KubeConfigPath))
        {
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(opts.KubeConfigPath);
        }
        else
        {
            config = BuildFallbackConfig(opts);
        }

        var defaultClient = new k8s.Kubernetes(config);
        defaultClient.HttpClient.Timeout = TimeSpan.FromSeconds(10);
        return defaultClient;
    }

    private static KubernetesClientConfiguration BuildFallbackConfig(KubernetesConfigOptions opts)
    {
        try
        {
            return KubernetesClientConfiguration.BuildDefaultConfig();
        }
        catch
        {
            return new KubernetesClientConfiguration
            {
                Host = opts.MasterUri ?? "http://localhost:8080"
            };
        }
    }
}

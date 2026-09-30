using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public class RegistryCredentialStore : IRegistryCredentialStore
{
    private readonly ILogger<RegistryCredentialStore> _logger;
    private readonly ConcurrentDictionary<string, (string Username, string Password)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RegistryCredentialStore(ILogger<RegistryCredentialStore> logger)
    {
        _logger = logger;
    }

    public void RegisterCredentials(string host, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(password)) return;
        var normalizedHost = NormalizeHost(host);
        _cache[normalizedHost] = (username?.Trim() ?? "", password.Trim());

        // Alias common Docker Hub variants
        if (IsDockerHub(normalizedHost))
        {
            _cache["docker.io"] = (username?.Trim() ?? "", password.Trim());
            _cache["registry-1.docker.io"] = (username?.Trim() ?? "", password.Trim());
            _cache["index.docker.io"] = (username?.Trim() ?? "", password.Trim());
        }
    }

    public void RegisterDockerConfig(string dockerConfigJson)
    {
        if (string.IsNullOrWhiteSpace(dockerConfigJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(dockerConfigJson);
            if (!doc.RootElement.TryGetProperty("auths", out var authsProp) || authsProp.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var prop in authsProp.EnumerateObject())
            {
                var host = prop.Name;
                var elem = prop.Value;

                string? user = null;
                string? pass = null;

                if (elem.TryGetProperty("username", out var u) && elem.TryGetProperty("password", out var p))
                {
                    user = u.GetString();
                    pass = p.GetString();
                }

                if (string.IsNullOrWhiteSpace(pass) && elem.TryGetProperty("auth", out var a))
                {
                    var authStr = a.GetString();
                    if (!string.IsNullOrWhiteSpace(authStr))
                    {
                        try
                        {
                            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authStr));
                            var colonIdx = decoded.IndexOf(':');
                            if (colonIdx >= 0)
                            {
                                user = decoded.Substring(0, colonIdx);
                                pass = decoded.Substring(colonIdx + 1);
                            }
                        }
                        catch
                        {
                            // Ignore invalid base64 in auth property
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(pass))
                {
                    RegisterCredentials(host, user ?? "", pass);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse Docker config JSON during credential registration");
        }
    }

    public (string Username, string Password)? TryGetCredentials(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var normalizedHost = NormalizeHost(host);

        // 1. In-memory registered cache
        if (_cache.TryGetValue(normalizedHost, out var creds))
        {
            return creds;
        }

        // 2. Check environment variables
        var envCreds = TryGetFromEnvironment(normalizedHost);
        if (envCreds != null)
        {
            _cache[normalizedHost] = envCreds.Value;
            return envCreds;
        }

        // 3. Check host config files (helm/registry/config.json or .docker/config.json)
        var fileCreds = TryGetFromHostConfigFiles(normalizedHost);
        if (fileCreds != null)
        {
            _cache[normalizedHost] = fileCreds.Value;
            return fileCreds;
        }

        return null;
    }

    public string GenerateDockerConfigJson(string host, string username, string password)
    {
        var cleanHost = NormalizeHost(host);
        var cleanUser = username?.Trim() ?? "";
        var cleanPass = password?.Trim() ?? "";
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cleanUser}:{cleanPass}"));

        var auths = new Dictionary<string, object>
        {
            [cleanHost] = new { username = cleanUser, password = cleanPass, auth = authString },
            [$"https://{cleanHost}"] = new { username = cleanUser, password = cleanPass, auth = authString },
            [$"https://{cleanHost}/v2/"] = new { username = cleanUser, password = cleanPass, auth = authString }
        };

        if (IsDockerHub(cleanHost))
        {
            auths["docker.io"] = new { username = cleanUser, password = cleanPass, auth = authString };
            auths["registry-1.docker.io"] = new { username = cleanUser, password = cleanPass, auth = authString };
            auths["https://index.docker.io/v1/"] = new { username = cleanUser, password = cleanPass, auth = authString };
        }

        return JsonSerializer.Serialize(new { auths }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string NormalizeHost(string hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl)) return "";
        var raw = hostOrUrl.Trim();

        if (raw.StartsWith("oci://", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring("oci://".Length);
        else if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring("https://".Length);
        else if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring("http://".Length);

        var slashIdx = raw.IndexOf('/');
        if (slashIdx > 0)
        {
            raw = raw.Substring(0, slashIdx);
        }

        return raw.Trim().ToLowerInvariant();
    }

    private static bool IsDockerHub(string host) =>
        host.Equals("docker.io", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase);

    private (string Username, string Password)? TryGetFromEnvironment(string normalizedHost)
    {
        try
        {
            if (normalizedHost.Equals("ghcr.io", StringComparison.OrdinalIgnoreCase))
            {
                var ghToken = Environment.GetEnvironmentVariable("GHCR_TOKEN")
                           ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (!string.IsNullOrWhiteSpace(ghToken))
                {
                    var ghUser = Environment.GetEnvironmentVariable("GITHUB_ACTOR")
                              ?? Environment.GetEnvironmentVariable("GHCR_USER")
                              ?? "token";
                    return (ghUser, ghToken.Trim());
                }
            }

            var dockerAuthConfig = Environment.GetEnvironmentVariable("DOCKER_AUTH_CONFIG");
            if (!string.IsNullOrWhiteSpace(dockerAuthConfig))
            {
                RegisterDockerConfig(dockerAuthConfig);
                if (_cache.TryGetValue(normalizedHost, out var found))
                {
                    return found;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read credentials from environment for host {Host}", normalizedHost);
        }

        return null;
    }

    private (string Username, string Password)? TryGetFromHostConfigFiles(string normalizedHost)
    {
        var candidatePaths = new List<string>();

        var helmRegistryConfig = Environment.GetEnvironmentVariable("HELM_REGISTRY_CONFIG");
        if (!string.IsNullOrWhiteSpace(helmRegistryConfig)) candidatePaths.Add(helmRegistryConfig);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            candidatePaths.Add(Path.Combine(home, ".config", "helm", "registry", "config.json"));

            var dockerConfigEnv = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
            if (!string.IsNullOrWhiteSpace(dockerConfigEnv))
            {
                candidatePaths.Add(Path.Combine(dockerConfigEnv, "config.json"));
            }
            candidatePaths.Add(Path.Combine(home, ".docker", "config.json"));
        }

        foreach (var path in candidatePaths.Distinct())
        {
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    RegisterDockerConfig(content);
                    if (_cache.TryGetValue(normalizedHost, out var creds))
                    {
                        return creds;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to read host registry config from {Path}", path);
                }
            }
        }

        return null;
    }
}

namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

/// <summary>
/// Provides secure credential resolution and Docker/OCI config generation for private Helm repositories and OCI registries.
/// </summary>
public interface IRegistryCredentialStore
{
    /// <summary>
    /// Registers or updates cached credentials for an OCI registry or Helm repository host.
    /// </summary>
    void RegisterCredentials(string host, string username, string password);

    /// <summary>
    /// Ingests credentials from a raw Docker config JSON string (e.g. from a kubernetes.io/dockerconfigjson secret).
    /// </summary>
    void RegisterDockerConfig(string dockerConfigJson);

    /// <summary>
    /// Attempts to retrieve credentials for a registry host (e.g. "ghcr.io", "docker.io").
    /// Checks in-memory cache, environment variables, and host Docker/Helm config files.
    /// </summary>
    (string Username, string Password)? TryGetCredentials(string host);

    /// <summary>
    /// Generates a valid Docker config JSON string with auth entries for the specified host and credentials.
    /// </summary>
    string GenerateDockerConfigJson(string host, string username, string password);
}

using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using Xunit;

namespace ControlPlane.Api.Tests;

public class HelmClientTests
{
    [Fact]
    public void SanitizeHelmOutput_RemovesInsecureKubeconfigWarnings()
    {
        var rawOutput = """
        WARNING: Kubernetes configuration file is group-readable. This is insecure. Location: /tmp/helm-kc-1234.yaml
        WARNING: Kubernetes configuration file is world-readable. This is insecure. Location: /tmp/helm-kc-1234.yaml
        Error: failed pre-install: 1 error occurred:
            * job zitadel-init failed: DeadlineExceeded
        """;

        var sanitized = HelmClient.SanitizeHelmOutput(rawOutput);

        Assert.DoesNotContain("WARNING: Kubernetes configuration file is group-readable", sanitized);
        Assert.DoesNotContain("WARNING: Kubernetes configuration file is world-readable", sanitized);
        Assert.Contains("job zitadel-init failed: DeadlineExceeded", sanitized);
    }

    [Fact]
    public void SanitizeHelmOutput_PreservesNormalOutput()
    {
        var normal = "Release 'zitadel' installed/upgraded successfully.\nSTATUS: deployed";
        var sanitized = HelmClient.SanitizeHelmOutput(normal);
        Assert.Equal(normal, sanitized);
    }

    [Fact]
    public void FindKubectlPath_ExecutesWithoutException()
    {
        // Should not throw, returns null or valid path string
        var path = HelmClient.FindKubectlPath();
        if (path != null)
        {
            Assert.True(File.Exists(path));
        }
    }

    [Fact]
    public void BuildInstallArguments_IncludesReuseValues_WhenRequested()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "my-app",
            Namespace: "prod",
            ChartName: "my-chart",
            ReuseValues: true
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("--reuse-values", args);
        Assert.DoesNotContain("--reset-values", args);
        Assert.Contains("upgrade --install \"my-app\" \"my-chart\"", args);
    }

    [Fact]
    public void BuildInstallArguments_IncludesResetValues_WhenRequested()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "my-app",
            Namespace: "prod",
            ChartName: "my-chart",
            ResetValues: true
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("--reset-values", args);
        Assert.DoesNotContain("--reuse-values", args);
    }

    [Fact]
    public void BuildInstallArguments_IncludesValuesFile_WhenProvided()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "my-app",
            Namespace: "prod",
            ChartName: "my-chart"
        );

        var args = HelmClient.BuildInstallArguments(request, tempValuesFile: "/tmp/helm-vals-123.yaml");

        Assert.Contains("--values \"/tmp/helm-vals-123.yaml\"", args);
    }

    [Fact]
    public void BuildInstallArguments_HandlesOciRepoUrl_WithoutRepoFlag()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "controlplane",
            Namespace: "controlplane",
            ChartName: "controlplane",
            RepoUrl: "oci://ghcr.io/ckchessmaster/charts/controlplane",
            Version: "1.2.0"
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("upgrade --install \"controlplane\" \"oci://ghcr.io/ckchessmaster/charts/controlplane\"", args);
        Assert.DoesNotContain("--repo", args);
        Assert.Contains("--version \"1.2.0\"", args);
    }

    [Fact]
    public void BuildInstallArguments_HandlesOciBaseRepoUrl_AppendsChartName()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "controlplane",
            Namespace: "controlplane",
            ChartName: "controlplane",
            RepoUrl: "oci://ghcr.io/ckchessmaster/charts",
            Version: "1.2.0"
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("upgrade --install \"controlplane\" \"oci://ghcr.io/ckchessmaster/charts/controlplane\"", args);
        Assert.DoesNotContain("--repo", args);
    }

    [Fact]
    public void BuildInstallArguments_HandlesDirectOciChartName_WithoutRepoFlag()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "controlplane",
            Namespace: "controlplane",
            ChartName: "oci://ghcr.io/ckchessmaster/charts/controlplane",
            Version: "1.2.0"
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("upgrade --install \"controlplane\" \"oci://ghcr.io/ckchessmaster/charts/controlplane\"", args);
        Assert.DoesNotContain("--repo", args);
    }

    [Fact]
    public void BuildInstallArguments_IncludesRegistryConfig_WhenProvided()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "trading-platform",
            Namespace: "trading",
            ChartName: "oci://ghcr.io/ckchessmaster/charts/trading-platform",
            Version: "1.0.0"
        );

        var args = HelmClient.BuildInstallArguments(request, tempRegistryConfigFile: "/tmp/helm-reg-999.json");

        Assert.Contains("--registry-config \"/tmp/helm-reg-999.json\"", args);
        Assert.DoesNotContain("--username", args);
        Assert.DoesNotContain("--password", args);
    }

    [Fact]
    public void BuildInstallArguments_IncludesUsernameAndPassword_ForClassicHttpRepo()
    {
        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "my-app",
            Namespace: "prod",
            ChartName: "my-app",
            RepoUrl: "https://nexus.homelab.local/repository/helm-private",
            RegistryUsername: "admin",
            RegistryPassword: "secret-password"
        );

        var args = HelmClient.BuildInstallArguments(request);

        Assert.Contains("--repo \"https://nexus.homelab.local/repository/helm-private\"", args);
        Assert.Contains("--username \"admin\"", args);
        Assert.Contains("--password \"secret-password\"", args);
        Assert.Contains("--pass-credentials", args);
    }

    [Fact]
    public void RegistryCredentialStore_GeneratesValidDockerConfigJson()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryCredentialStore>.Instance;
        var store = new RegistryCredentialStore(logger);

        var json = store.GenerateDockerConfigJson("ghcr.io", "ckchessmaster", "ghp_mocktoken123");

        Assert.Contains("\"ghcr.io\"", json);
        Assert.Contains("\"https://ghcr.io\"", json);
        Assert.Contains("\"ckchessmaster\"", json);
        Assert.Contains("\"ghp_mocktoken123\"", json);
    }

    [Fact]
    public void RegistryCredentialStore_NormalizesHostnames_AndParsesDockerConfig()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistryCredentialStore>.Instance;
        var store = new RegistryCredentialStore(logger);

        var rawConfig = """
        {
          "auths": {
            "ghcr.io": {
              "username": "testuser",
              "password": "testpassword"
            }
          }
        }
        """;

        store.RegisterDockerConfig(rawConfig);
        var creds = store.TryGetCredentials("oci://ghcr.io/myorg/myrepo");

        Assert.NotNull(creds);
        Assert.Equal("testuser", creds.Value.Username);
        Assert.Equal("testpassword", creds.Value.Password);
    }

    [Fact]
    public void SanitizeHelmOutput_MasksProvidedPassword()
    {
        var output = "Error: Authentication failed for secret-pat-12345 when connecting to ghcr.io";
        var sanitized = HelmClient.SanitizeHelmOutput(output, "secret-pat-12345");

        Assert.DoesNotContain("secret-pat-12345", sanitized);
        Assert.Contains("********", sanitized);
    }
}

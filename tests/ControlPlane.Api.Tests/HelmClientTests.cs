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
}

using System.Runtime.CompilerServices;

namespace ControlPlane.Api.Tests;

public static class TestAssemblySetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var distDir = Path.Combine(AppContext.BaseDirectory, "agent-dist");
        Directory.CreateDirectory(distDir);

        var binaryNames = new[]
        {
            "controlplane-agent-linux-amd64",
            "controlplane-agent-linux-arm64",
            "controlplane-agent-windows-amd64.exe"
        };

        foreach (var name in binaryNames)
        {
            var targetPath = Path.Combine(distDir, name);
            if (!File.Exists(targetPath) || new FileInfo(targetPath).Length < 1024)
            {
                File.WriteAllBytes(targetPath, new byte[4096]);
            }
        }
    }
}

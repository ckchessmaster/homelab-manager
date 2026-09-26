using System.Text.Json;
using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Configuration;

namespace ControlPlane.Api.Features.Agents;

public class AgentBinaryService
{
    public const string CurrentAgentVersion = "1.3.1";
    public const string DefaultAgentVersion = "1.3.1";

    private readonly IConfiguration? _configuration;

    public AgentBinaryService() : this(null)
    {
    }

    public AgentBinaryService(IConfiguration? configuration)
    {
        _configuration = configuration;
    }

    public virtual string GetDistDirectory()
    {
        var configuredDir = _configuration?["ControlPlane:AgentDistDir"] ?? Environment.GetEnvironmentVariable("AGENT_DIST_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            Directory.CreateDirectory(configuredDir);
            return configuredDir;
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, "agent-dist");
        try
        {
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }
        catch
        {
            var fallback = Path.Combine(Path.GetTempPath(), "controlplane-agent-dist");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public virtual string GetTargetVersion()
    {
        try
        {
            var distDir = GetDistDirectory();
            var versionFile = Path.Combine(distDir, "version.json");
            if (File.Exists(versionFile))
            {
                var json = File.ReadAllText(versionFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var prop) && !string.IsNullOrWhiteSpace(prop.GetString()))
                {
                    return prop.GetString()!.TrimStart('v');
                }
            }
        }
        catch
        {
            // Fall back to default version
        }

        return DefaultAgentVersion;
    }

    public virtual string? GetBinaryPath(string arch)
    {
        var normalizedArch = arch.ToLowerInvariant().Replace("_", "-");
        var filename = normalizedArch switch
        {
            "linux-arm64" or "aarch64" or "arm64" => "controlplane-agent-linux-arm64",
            "windows-amd64" or "windows-x64" or "win-x64" or "windows" => "controlplane-agent-windows-amd64.exe",
            _ => "controlplane-agent-linux-amd64"
        };

        var configuredDir = _configuration?["ControlPlane:AgentDistDir"] ?? Environment.GetEnvironmentVariable("AGENT_DIST_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            var customPath = Path.Combine(configuredDir, filename);
            if (File.Exists(customPath)) return customPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "agent-dist", filename),
            Path.Combine(AppContext.BaseDirectory, "../../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "src/agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "agent/dist", filename)
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, "src", "agent", "dist", filename);
            if (File.Exists(testPath)) return testPath;
            current = current.Parent;
        }

        return null;
    }

    public virtual IReadOnlyList<AgentBinaryPlatformDto> GetPlatformDetails()
    {
        var platforms = new[]
        {
            ("linux-amd64", "Linux AMD64 (x86_64)", "controlplane-agent-linux-amd64"),
            ("linux-arm64", "Linux ARM64 (aarch64)", "controlplane-agent-linux-arm64"),
            ("windows-amd64", "Windows AMD64 (x64)", "controlplane-agent-windows-amd64.exe")
        };

        var result = new List<AgentBinaryPlatformDto>();
        foreach (var (arch, name, fileName) in platforms)
        {
            var path = GetBinaryPath(arch);
            var exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            long? size = null;
            DateTimeOffset? lastModified = null;

            if (exists && path != null)
            {
                try
                {
                    var fi = new FileInfo(path);
                    size = fi.Length;
                    lastModified = fi.LastWriteTimeUtc;
                }
                catch
                {
                    // Ignore metadata read errors
                }
            }

            result.Add(new AgentBinaryPlatformDto(
                Architecture: arch,
                DisplayName: name,
                FileName: fileName,
                IsAvailable: exists,
                SizeBytes: size,
                LastModifiedAtUtc: lastModified,
                DownloadPath: $"/api/v1/agents/binaries/{arch}"
            ));
        }

        return result;
    }

    public virtual IReadOnlyList<string> GetAvailableArchitectures()
    {
        var archs = new List<string>();
        if (GetBinaryPath("linux-amd64") != null) archs.Add("linux-amd64");
        if (GetBinaryPath("linux-arm64") != null) archs.Add("linux-arm64");
        if (GetBinaryPath("windows-amd64") != null) archs.Add("windows-amd64");
        return archs;
    }
}

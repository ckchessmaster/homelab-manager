namespace ControlPlane.Api.Features.Agents;

public class AgentBinaryService
{
    public const string CurrentAgentVersion = "1.1.0";

    public virtual string? GetBinaryPath(string arch)
    {
        var normalizedArch = arch.ToLowerInvariant().Replace("_", "-");
        var filename = normalizedArch switch
        {
            "linux-arm64" or "aarch64" or "arm64" => "controlplane-agent-linux-arm64",
            _ => "controlplane-agent-linux-amd64"
        };

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "agent-dist", filename),
            Path.Combine(AppContext.BaseDirectory, "../../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "src/agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "../agent/dist", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "agent/dist", filename),
            $"/home/ckingdon/projects/homelab-manager/src/agent/dist/{filename}"
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

    public virtual IReadOnlyList<string> GetAvailableArchitectures()
    {
        var archs = new List<string>();
        if (GetBinaryPath("linux-amd64") != null) archs.Add("linux-amd64");
        if (GetBinaryPath("linux-arm64") != null) archs.Add("linux-arm64");
        return archs;
    }
}

namespace ControlPlane.Cli.Temporal;

public record TemporalDevServerConfig(
    bool Enabled = true,
    int GrpcPort = 7233,
    int UiPort = 8233,
    string DbFilename = "",
    string Ip = "127.0.0.1",
    string? CustomBinaryPath = null,
    bool Headless = false,
    string Namespace = "homelab-manager"
);

public interface ITemporalDevServerManager : IAsyncDisposable
{
    Task<bool> EnsureRunningAsync(TemporalDevServerConfig config, CancellationToken ct = default);
    Task<bool> IsServerReachableAsync(string host, int port, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    string BuildCommandLineArgs(TemporalDevServerConfig config);
    Task<string?> ResolveOrDownloadBinaryAsync(string? customPath = null, CancellationToken ct = default);
}

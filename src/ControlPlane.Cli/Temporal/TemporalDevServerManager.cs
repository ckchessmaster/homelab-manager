using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Cli.Temporal;

public class TemporalDevServerManager : ITemporalDevServerManager
{
    private readonly ILogger<TemporalDevServerManager>? _logger;
    private readonly HttpClient _httpClient;
    private Process? _process;

    public TemporalDevServerManager(ILogger<TemporalDevServerManager>? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string BuildCommandLineArgs(TemporalDevServerConfig config)
    {
        var args = $"server start-dev --ip {config.Ip} --port {config.GrpcPort} --ui-port {config.UiPort}";
        if (!string.IsNullOrWhiteSpace(config.Namespace))
        {
            args += $" --namespace {config.Namespace}";
        }
        if (!string.IsNullOrWhiteSpace(config.DbFilename))
        {
            args += $" --db-filename \"{config.DbFilename}\"";
        }
        if (config.Headless)
        {
            args += " --headless";
        }
        return args;
    }

    public async Task<bool> IsServerReachableAsync(string host, int port, CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(750));

            await tcp.ConnectAsync(host, port, timeoutCts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> ResolveOrDownloadBinaryAsync(string? customPath = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            _logger?.LogInformation("Using custom Temporal binary: {Path}", customPath);
            return customPath;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var binaryName = isWindows ? "temporal.exe" : "temporal";

        // 1. Check local ~/.controlplane/bin/
        var binDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlplane", "bin");
        var localBin = Path.Combine(binDir, binaryName);
        if (File.Exists(localBin))
        {
            _logger?.LogInformation("Found cached Temporal CLI binary: {Path}", localBin);
            return localBin;
        }

        // 2. Check PATH
        var pathBinary = FindInPath(binaryName);
        if (!string.IsNullOrWhiteSpace(pathBinary))
        {
            _logger?.LogInformation("Found Temporal CLI in system PATH: {Path}", pathBinary);
            return pathBinary;
        }

        // 3. Attempt download
        _logger?.LogInformation("Temporal CLI binary not found locally or in PATH. Attempting automatic download...");
        return await DownloadBinaryAsync(binDir, binaryName, isWindows, ct);
    }

    private static string? FindInPath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fullPath = Path.Combine(dir, binaryName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private async Task<string?> DownloadBinaryAsync(string binDir, string binaryName, bool isWindows, CancellationToken ct)
    {
        string platform;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) platform = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) platform = "darwin";
        else if (isWindows) platform = "windows";
        else
        {
            _logger?.LogWarning("Unsupported OS platform for automatic Temporal CLI download: {OS}", RuntimeInformation.OSDescription);
            return null;
        }

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => ""
        };

        if (string.IsNullOrEmpty(arch))
        {
            _logger?.LogWarning("Unsupported architecture for automatic Temporal CLI download: {Arch}", RuntimeInformation.ProcessArchitecture);
            return null;
        }

        var downloadUrl = $"https://temporal.download/cli/archive/latest?platform={platform}&arch={arch}";
        Directory.CreateDirectory(binDir);
        var targetBinaryPath = Path.Combine(binDir, binaryName);

        try
        {
            _logger?.LogInformation("Downloading Temporal CLI from {Url}...", downloadUrl);
            var tempArchive = Path.GetTempFileName();
            try
            {
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    using var fs = File.Create(tempArchive);
                    await response.Content.CopyToAsync(fs, ct);
                }

                if (isWindows)
                {
                    using var zip = ZipFile.OpenRead(tempArchive);
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(binaryName, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        entry.ExtractToFile(targetBinaryPath, overwrite: true);
                    }
                }
                else
                {
                    using var fs = File.OpenRead(tempArchive);
                    using var gz = new GZipStream(fs, CompressionMode.Decompress);
                    using var tar = new TarReader(gz);
                    while (tar.GetNextEntry() is { } entry)
                    {
                        if (entry.Name.EndsWith(binaryName, StringComparison.Ordinal) || entry.Name.EndsWith("/" + binaryName, StringComparison.Ordinal))
                        {
                            entry.ExtractToFile(targetBinaryPath, overwrite: true);
                            break;
                        }
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        try
                        {
                            File.SetUnixFileMode(targetBinaryPath,
                                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Could not set Unix executable mode on {Path}", targetBinaryPath);
                        }
                    }
                }

                if (File.Exists(targetBinaryPath))
                {
                    _logger?.LogInformation("Temporal CLI binary successfully downloaded to: {Path}", targetBinaryPath);
                    return targetBinaryPath;
                }
            }
            finally
            {
                if (File.Exists(tempArchive))
                {
                    try { File.Delete(tempArchive); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to download Temporal CLI binary from {Url}", downloadUrl);
        }

        return null;
    }

    public async Task<bool> EnsureRunningAsync(TemporalDevServerConfig config, CancellationToken ct = default)
    {
        if (!config.Enabled)
        {
            _logger?.LogInformation("Temporal Dev Server is disabled by configuration.");
            return false;
        }

        // Check if already reachable
        if (await IsServerReachableAsync(config.Ip, config.GrpcPort, ct))
        {
            _logger?.LogInformation("Temporal server is already running and reachable at {Host}:{Port}", config.Ip, config.GrpcPort);
            return true;
        }

        var binaryPath = await ResolveOrDownloadBinaryAsync(config.CustomBinaryPath, ct);
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            _logger?.LogWarning("Cannot start managed Temporal dev server: Temporal CLI binary could not be found or downloaded.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(config.DbFilename))
        {
            var dbDir = Path.GetDirectoryName(config.DbFilename);
            if (!string.IsNullOrWhiteSpace(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }
        }

        var args = BuildCommandLineArgs(config);
        _logger?.LogInformation("Starting managed Temporal dev server: {Binary} {Args}", binaryPath, args);

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            _process = Process.Start(startInfo);
            if (_process == null)
            {
                _logger?.LogError("Failed to start Temporal dev server process.");
                return false;
            }

            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger?.LogDebug("[TEMPORAL-DEV] {Line}", e.Data);
                }
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger?.LogDebug("[TEMPORAL-DEV:ERR] {Line}", e.Data);
                }
            };

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Poll until server is reachable (up to 20 seconds)
            var pollInterval = TimeSpan.FromMilliseconds(500);
            var maxAttempts = 40;

            for (int i = 0; i < maxAttempts; i++)
            {
                if (_process.HasExited)
                {
                    _logger?.LogError("Temporal dev server process exited unexpectedly with code {ExitCode}", _process.ExitCode);
                    return false;
                }

                if (await IsServerReachableAsync(config.Ip, config.GrpcPort, ct))
                {
                    _logger?.LogInformation("Managed Temporal dev server is active and receptive at {Ip}:{Port} (UI: http://{Ip}:{UiPort})",
                        config.Ip, config.GrpcPort, config.Ip, config.UiPort);
                    return true;
                }

                await Task.Delay(pollInterval, ct);
            }

            _logger?.LogWarning("Timed out waiting for Temporal dev server to listen on port {Port}.", config.GrpcPort);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting managed Temporal dev server process.");
            return false;
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _logger?.LogInformation("Terminating managed Temporal dev server process (PID: {Pid})...", _process.Id);
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Exception while terminating Temporal dev server process.");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

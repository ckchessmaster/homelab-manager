using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

public class HelmClient : IHelmClient
{
    private readonly ILogger<HelmClient> _logger;

    public HelmClient(ILogger<HelmClient> logger)
    {
        _logger = logger;
    }

    public static string? FindHelmPath()
    {
        var customPath = Environment.GetEnvironmentVariable("HELM_PATH");
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBin = Path.Combine(homeDir, ".local", "bin", "helm");
        if (File.Exists(localBin))
            return localBin;

        if (File.Exists("/usr/local/bin/helm"))
            return "/usr/local/bin/helm";

        if (File.Exists("/usr/bin/helm"))
            return "/usr/bin/helm";

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                var candidate = Path.Combine(p, "helm");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public static string? FindKubectlPath()
    {
        var customPath = Environment.GetEnvironmentVariable("KUBECTL_PATH");
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBin = Path.Combine(homeDir, ".local", "bin", "kubectl");
        if (File.Exists(localBin))
            return localBin;

        if (File.Exists("/usr/local/bin/kubectl"))
            return "/usr/local/bin/kubectl";

        if (File.Exists("/usr/bin/kubectl"))
            return "/usr/bin/kubectl";

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                var candidate = Path.Combine(p, "kubectl");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public static string BuildInstallArguments(InstallHelmReleaseRequestDto request, string? tempKubeconfig = null, string? tempValuesFile = null)
    {
        var args = new StringBuilder();
        args.Append($"upgrade --install \"{request.ReleaseName}\" \"{request.ChartName}\"");

        if (!string.IsNullOrWhiteSpace(request.RepoUrl))
        {
            args.Append($" --repo \"{request.RepoUrl}\"");
        }

        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            args.Append($" --version \"{request.Version}\"");
        }

        args.Append($" --namespace \"{request.Namespace}\"");

        if (request.CreateNamespace)
        {
            args.Append(" --create-namespace");
        }

        if (request.Wait)
        {
            args.Append(" --wait");
        }

        var timeoutSec = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 300;
        args.Append($" --timeout {timeoutSec}s");

        if (request.ResetValues)
        {
            args.Append(" --reset-values");
        }
        else if (request.ReuseValues)
        {
            args.Append(" --reuse-values");
        }

        if (!string.IsNullOrWhiteSpace(tempValuesFile))
        {
            args.Append($" --values \"{tempValuesFile}\"");
        }

        if (tempKubeconfig != null)
        {
            args.Append($" --kubeconfig \"{tempKubeconfig}\"");
        }

        return args.ToString();
    }

    public async Task<List<HelmReleaseSummaryDto>> ListReleasesAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null)
        {
            _logger.LogWarning("Helm CLI executable not found on host.");
            return new List<HelmReleaseSummaryDto>();
        }

        var (tempKubeconfig, cleanup) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        try
        {
            var args = new StringBuilder("list -a -o json");
            if (tempKubeconfig != null)
            {
                args.Append($" --kubeconfig \"{tempKubeconfig}\"");
            }

            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                args.Append(" -A");
            }
            else
            {
                args.Append($" -n \"{namespaceName}\"");
            }

            var (exitCode, stdout, stderr) = await RunCommandAsync(helmPath, args.ToString(), 30, ct);
            if (exitCode != 0)
            {
                _logger.LogWarning("helm list returned non-zero exit code {ExitCode}: {Error}", exitCode, stderr);
                return new List<HelmReleaseSummaryDto>();
            }

            if (string.IsNullOrWhiteSpace(stdout) || stdout.Trim() == "[]")
            {
                return new List<HelmReleaseSummaryDto>();
            }

            var entries = JsonSerializer.Deserialize<List<RawHelmListEntry>>(stdout);
            if (entries == null) return new List<HelmReleaseSummaryDto>();

            return entries.Select(e =>
            {
                var (chartName, chartVer) = ParseChartField(e.Chart);
                var rev = ParseRevision(e.Revision);
                var dt = ParseHelmDateTime(e.Updated);

                return new HelmReleaseSummaryDto(
                    Name: e.Name ?? "",
                    Namespace: e.Namespace ?? "",
                    Revision: rev,
                    Updated: dt,
                    Status: e.Status ?? "unknown",
                    Chart: e.Chart ?? "",
                    ChartName: chartName,
                    ChartVersion: chartVer,
                    AppVersion: e.AppVersion ?? "",
                    Description: e.Description
                );
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Helm releases");
            return new List<HelmReleaseSummaryDto>();
        }
        finally
        {
            cleanup();
        }
    }

    public async Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
        => await GetReleaseDetailAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, namespaceName, releaseName, null, ct);

    public async Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        int? revision,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null) return null;

        var (tempKubeconfig, cleanup) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        try
        {
            var kcArg = tempKubeconfig != null ? $"--kubeconfig \"{tempKubeconfig}\"" : "";
            var revArg = revision.HasValue && revision.Value > 0 ? $"--revision {revision.Value}" : "";

            // 1. Get Release metadata via list filter
            var listArgs = $"list -a -n \"{namespaceName}\" -f \"^{Regex.Escape(releaseName)}$\" -o json {kcArg}";
            var (_, listOut, _) = await RunCommandAsync(helmPath, listArgs, 30, ct);

            RawHelmListEntry? entry = null;
            if (!string.IsNullOrWhiteSpace(listOut))
            {
                var list = JsonSerializer.Deserialize<List<RawHelmListEntry>>(listOut);
                entry = list?.FirstOrDefault(e => string.Equals(e.Name, releaseName, StringComparison.OrdinalIgnoreCase));
            }

            if (entry == null) return null;

            // 2. Get user values
            var valArgs = $"get values \"{releaseName}\" -n \"{namespaceName}\" {revArg} -o yaml {kcArg}";
            var (valExit, valOut, _) = await RunCommandAsync(helmPath, valArgs, 30, ct);
            var valuesYaml = valExit == 0 ? (valOut.Trim() == "{}" ? null : valOut) : null;

            // 2b. Get computed (all) values
            var allValArgs = $"get values \"{releaseName}\" -n \"{namespaceName}\" -a {revArg} -o yaml {kcArg}";
            var (allExit, allOut, _) = await RunCommandAsync(helmPath, allValArgs, 30, ct);
            var computedValuesYaml = allExit == 0 ? (allOut.Trim() == "{}" ? null : allOut) : null;

            // 3. Get manifest
            var manArgs = $"get manifest \"{releaseName}\" -n \"{namespaceName}\" {revArg} {kcArg}";
            var (manExit, manOut, _) = await RunCommandAsync(helmPath, manArgs, 30, ct);
            var manifest = manExit == 0 ? manOut : null;

            // 4. Get notes
            var notesArgs = $"get notes \"{releaseName}\" -n \"{namespaceName}\" {revArg} {kcArg}";
            var (notesExit, notesOut, _) = await RunCommandAsync(helmPath, notesArgs, 30, ct);
            var notes = notesExit == 0 ? notesOut : null;

            var (chartName, chartVer) = ParseChartField(entry.Chart);
            var rev = revision ?? ParseRevision(entry.Revision);
            var dt = ParseHelmDateTime(entry.Updated);

            var catalogItem = HelmCatalogService.CuratedCatalog.FirstOrDefault(c =>
                string.Equals(c.ChartName, chartName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Id, chartName, StringComparison.OrdinalIgnoreCase));
            var repoUrl = catalogItem?.RepoUrl;

            return new HelmReleaseDetailDto(
                Name: entry.Name ?? releaseName,
                Namespace: entry.Namespace ?? namespaceName,
                Revision: rev,
                Updated: dt,
                Status: entry.Status ?? "unknown",
                Chart: entry.Chart ?? "",
                ChartName: chartName,
                ChartVersion: chartVer,
                AppVersion: entry.AppVersion ?? "",
                Description: entry.Description,
                Notes: notes,
                ValuesYaml: valuesYaml,
                Manifest: manifest,
                RepoUrl: repoUrl,
                ComputedValuesYaml: computedValuesYaml
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Helm release details for '{Namespace}/{Release}'", namespaceName, releaseName);
            return null;
        }
        finally
        {
            cleanup();
        }
    }

    public async Task<List<HelmReleaseRevisionDto>> GetReleaseHistoryAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null) return new List<HelmReleaseRevisionDto>();

        var (tempKubeconfig, cleanup) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        try
        {
            var kcArg = tempKubeconfig != null ? $"--kubeconfig \"{tempKubeconfig}\"" : "";
            var args = $"history \"{releaseName}\" -n \"{namespaceName}\" -o json {kcArg}";
            var (exitCode, stdout, stderr) = await RunCommandAsync(helmPath, args, 30, ct);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                _logger.LogWarning("helm history failed ({ExitCode}): {Error}", exitCode, stderr);
                return new List<HelmReleaseRevisionDto>();
            }

            var rawRevisions = JsonSerializer.Deserialize<List<RawHelmHistoryEntry>>(stdout);
            if (rawRevisions == null) return new List<HelmReleaseRevisionDto>();

            return rawRevisions.Select(r => new HelmReleaseRevisionDto(
                Revision: ParseRevision(r.Revision),
                Updated: ParseHelmDateTime(r.Updated),
                Status: r.Status ?? "unknown",
                Chart: r.Chart ?? "",
                AppVersion: r.AppVersion ?? "",
                Description: r.Description
            )).OrderByDescending(r => r.Revision).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Helm release history for '{Namespace}/{Release}'", namespaceName, releaseName);
            return new List<HelmReleaseRevisionDto>();
        }
        finally
        {
            cleanup();
        }
    }

    public async Task<HelmOperationResultDto> InstallOrUpgradeReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        InstallHelmReleaseRequestDto request,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null)
        {
            return new HelmOperationResultDto(false, "Helm CLI executable not found on host system.");
        }

        var (tempKubeconfig, cleanupKc) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        string? tempValuesFile = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(request.ValuesYaml))
            {
                tempValuesFile = Path.Combine(Path.GetTempPath(), $"helm-val-{Guid.NewGuid():N}.yaml");
                await File.WriteAllTextAsync(tempValuesFile, request.ValuesYaml, ct);
                SetRestrictedPermissions(tempValuesFile);
            }

            var argsStr = BuildInstallArguments(request, tempKubeconfig, tempValuesFile);

            _logger.LogInformation("Executing Helm upgrade --install for release '{Release}' in '{Namespace}'...",
                request.ReleaseName, request.Namespace);

            var timeoutSec = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 300;
            var (exitCode, stdout, stderr) = await RunCommandAsync(helmPath, argsStr, timeoutSec + 15, ct);

            if (exitCode == 0)
            {
                _logger.LogInformation("Helm upgrade --install for '{Release}' succeeded.", request.ReleaseName);
                return new HelmOperationResultDto(
                    Success: true,
                    Message: $"Release '{request.ReleaseName}' installed/upgraded successfully.",
                    ReleaseName: request.ReleaseName,
                    Output: stdout
                );
            }
            else
            {
                var errorMsg = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                errorMsg = SanitizeHelmOutput(errorMsg);

                // Attempt hook job diagnostic extraction if a Job failed
                var hookMatch = Regex.Match(errorMsg, @"job\s+([a-zA-Z0-9_-]+)\s+failed", RegexOptions.IgnoreCase);
                if (hookMatch.Success)
                {
                    var failedJobName = hookMatch.Groups[1].Value;
                    var kubectlPath = FindKubectlPath();
                    if (kubectlPath != null && tempKubeconfig != null)
                    {
                        var kcArg = $"--kubeconfig \"{tempKubeconfig}\"";
                        var logArgs = $"logs job/{failedJobName} -n \"{request.Namespace}\" --tail 50 {kcArg}";
                        var (logExit, logStdout, _) = await RunCommandAsync(kubectlPath, logArgs, 10, ct);
                        if (logExit == 0 && !string.IsNullOrWhiteSpace(logStdout))
                        {
                            errorMsg += $"\n\n--- Hook Job Pod Logs ({failedJobName}) ---\n{logStdout}";
                        }
                        else
                        {
                            var descArgs = $"describe job \"{failedJobName}\" -n \"{request.Namespace}\" {kcArg}";
                            var (descExit, descStdout, _) = await RunCommandAsync(kubectlPath, descArgs, 10, ct);
                            if (descExit == 0 && !string.IsNullOrWhiteSpace(descStdout))
                            {
                                var eventsIdx = descStdout.IndexOf("Events:", StringComparison.OrdinalIgnoreCase);
                                if (eventsIdx >= 0)
                                {
                                    errorMsg += $"\n\n--- Hook Job Events ({failedJobName}) ---\n{descStdout.Substring(eventsIdx).Trim()}";
                                }
                            }
                        }
                    }

                    errorMsg += $"\n\n[Action Required] The failed hook Job must be deleted before retrying:\n  kubectl delete job {failedJobName} -n \"{request.Namespace}\"";
                }

                _logger.LogError("Helm upgrade --install failed ({ExitCode}): {Error}", exitCode, errorMsg);
                return new HelmOperationResultDto(
                    Success: false,
                    Message: $"Helm operation failed (exit code {exitCode}): {errorMsg}",
                    ReleaseName: request.ReleaseName,
                    Output: errorMsg
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Helm upgrade --install for '{Release}'", request.ReleaseName);
            return new HelmOperationResultDto(false, $"Execution error: {ex.Message}", request.ReleaseName);
        }
        finally
        {
            cleanupKc();
            if (tempValuesFile != null && File.Exists(tempValuesFile))
            {
                try { File.Delete(tempValuesFile); } catch { /* ignore */ }
            }
        }
    }

    public async Task<HelmOperationResultDto> RollbackReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        int revision,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null)
        {
            return new HelmOperationResultDto(false, "Helm CLI executable not found on host system.");
        }

        var (tempKubeconfig, cleanupKc) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        try
        {
            var args = new StringBuilder();
            args.Append($"rollback \"{releaseName}\" {revision} --namespace \"{namespaceName}\"");

            if (tempKubeconfig != null)
            {
                args.Append($" --kubeconfig \"{tempKubeconfig}\"");
            }

            _logger.LogInformation("Rolling back Helm release '{Release}' to revision {Revision} in '{Namespace}'...",
                releaseName, revision, namespaceName);

            var (exitCode, stdout, stderr) = await RunCommandAsync(helmPath, args.ToString(), 120, ct);
            if (exitCode == 0)
            {
                return new HelmOperationResultDto(
                    Success: true,
                    Message: $"Release '{releaseName}' rolled back to revision {revision} successfully.",
                    ReleaseName: releaseName,
                    Revision: revision,
                    Output: stdout
                );
            }
            else
            {
                var errorMsg = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                return new HelmOperationResultDto(
                    Success: false,
                    Message: $"Helm rollback failed: {errorMsg}",
                    ReleaseName: releaseName,
                    Revision: revision,
                    Output: errorMsg
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback Helm release '{Release}'", releaseName);
            return new HelmOperationResultDto(false, $"Rollback error: {ex.Message}", releaseName, revision);
        }
        finally
        {
            cleanupKc();
        }
    }

    public async Task<HelmOperationResultDto> UninstallReleaseAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        var helmPath = FindHelmPath();
        if (helmPath == null)
        {
            return new HelmOperationResultDto(false, "Helm CLI executable not found on host system.");
        }

        var (tempKubeconfig, cleanupKc) = await PrepareKubeconfigFileAsync(kubeconfigYaml, apiServerUrl, token, skipTlsVerify, ct);
        try
        {
            var args = new StringBuilder();
            args.Append($"uninstall \"{releaseName}\" --namespace \"{namespaceName}\"");

            if (tempKubeconfig != null)
            {
                args.Append($" --kubeconfig \"{tempKubeconfig}\"");
            }

            _logger.LogInformation("Uninstalling Helm release '{Release}' from namespace '{Namespace}'...",
                releaseName, namespaceName);

            var (exitCode, stdout, stderr) = await RunCommandAsync(helmPath, args.ToString(), 120, ct);
            if (exitCode == 0)
            {
                return new HelmOperationResultDto(
                    Success: true,
                    Message: $"Release '{releaseName}' uninstalled successfully.",
                    ReleaseName: releaseName,
                    Output: stdout
                );
            }
            else
            {
                var errorMsg = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                return new HelmOperationResultDto(
                    Success: false,
                    Message: $"Helm uninstall failed: {errorMsg}",
                    ReleaseName: releaseName,
                    Output: errorMsg
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Helm release '{Release}'", releaseName);
            return new HelmOperationResultDto(false, $"Uninstall error: {ex.Message}", releaseName);
        }
        finally
        {
            cleanupKc();
        }
    }

    private static async Task<(string? TempPath, Action Cleanup)> PrepareKubeconfigFileAsync(
        string? kubeconfigYaml,
        string? apiServerUrl,
        string? token,
        bool skipTlsVerify,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(kubeconfigYaml))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"helm-kc-{Guid.NewGuid():N}.yaml");
            await File.WriteAllTextAsync(tempFile, kubeconfigYaml, ct);
            SetRestrictedPermissions(tempFile);
            return (tempFile, () =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
            });
        }

        if (!string.IsNullOrWhiteSpace(apiServerUrl))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"helm-kc-{Guid.NewGuid():N}.yaml");
            var insecureStr = skipTlsVerify ? "true" : "false";
            var synthesized = $$"""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - cluster:
                server: {{apiServerUrl}}
                insecure-skip-tls-verify: {{insecureStr}}
              name: dynamic-cluster
            contexts:
            - context:
                cluster: dynamic-cluster
                user: dynamic-user
              name: dynamic-context
            current-context: dynamic-context
            users:
            - name: dynamic-user
              user:
                token: {{token ?? ""}}
            """;
            await File.WriteAllTextAsync(tempFile, synthesized, ct);
            SetRestrictedPermissions(tempFile);
            return (tempFile, () =>
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
            });
        }

        return (null, () => { });
    }

    private static void SetRestrictedPermissions(string filePath)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(filePath))
        {
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Fallback / ignore if filesystem does not support Unix file permissions
            }
        }
    }

    public static string SanitizeHelmOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "";
        var lines = output.Split('\n');
        var filtered = lines.Where(l =>
            !l.Contains("WARNING: Kubernetes configuration file is", StringComparison.OrdinalIgnoreCase));
        return string.Join('\n', filtered).Trim();
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCommandAsync(
        string executable,
        string arguments,
        int timeoutSeconds,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            return (
                process.ExitCode,
                SanitizeHelmOutput(stdoutBuilder.ToString()),
                SanitizeHelmOutput(stderrBuilder.ToString())
            );
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* ignore */ }
            return (-1, SanitizeHelmOutput(stdoutBuilder.ToString()), "Operation timed out or was canceled.");
        }
    }

    private static (string Name, string Version) ParseChartField(string? chart)
    {
        if (string.IsNullOrWhiteSpace(chart)) return ("", "");
        var match = Regex.Match(chart, @"^(.*?)-(\d+.*)$");
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }
        return (chart, "");
    }

    private static int ParseRevision(object? revision)
    {
        if (revision == null) return 1;
        if (revision is int i) return i;
        if (revision is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var num)) return num;
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var sNum)) return sNum;
        }
        if (int.TryParse(revision.ToString(), out var parsed)) return parsed;
        return 1;
    }

    private static DateTimeOffset ParseHelmDateTime(string? updated)
    {
        if (string.IsNullOrWhiteSpace(updated)) return DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(updated, out var dto)) return dto;
        return DateTimeOffset.UtcNow;
    }

    private record RawHelmListEntry(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("namespace")] string? Namespace,
        [property: JsonPropertyName("revision")] object? Revision,
        [property: JsonPropertyName("updated")] string? Updated,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("chart")] string? Chart,
        [property: JsonPropertyName("app_version")] string? AppVersion,
        [property: JsonPropertyName("description")] string? Description
    );

    private record RawHelmHistoryEntry(
        [property: JsonPropertyName("revision")] object? Revision,
        [property: JsonPropertyName("updated")] string? Updated,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("chart")] string? Chart,
        [property: JsonPropertyName("app_version")] string? AppVersion,
        [property: JsonPropertyName("description")] string? Description
    );
}

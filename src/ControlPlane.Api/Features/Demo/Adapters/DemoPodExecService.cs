using System.Text;
using ControlPlane.Api.Features.Adapters.Kubernetes;

namespace ControlPlane.Api.Features.Demo.Adapters;

/// <summary>
/// Safe, in-memory simulated pod terminal shell used when <c>DEMO_MODE=true</c>.
/// Never executes commands on the host OS.
/// </summary>
public sealed class DemoPodExecService : IPodExecService
{
    private readonly IKubernetesAdapter? _kubernetesAdapter;

    public DemoPodExecService(IKubernetesAdapter? kubernetesAdapter = null)
    {
        _kubernetesAdapter = kubernetesAdapter;
    }

    public async Task<IPodExecSession> AttachAsync(
        PodExecRequest request,
        Func<string, Task> onOutput,
        Func<int, Task> onClosed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);
        ArgumentNullException.ThrowIfNull(onClosed);

        if (string.IsNullOrWhiteSpace(request.Namespace))
        {
            throw new ArgumentException("Namespace is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.PodName))
        {
            throw new ArgumentException("PodName is required.", nameof(request));
        }

        var namespaceName = request.Namespace.Trim();
        var podName = request.PodName.Trim();
        var clusterId = string.IsNullOrWhiteSpace(request.ClusterId) ? "demo-cluster" : request.ClusterId.Trim();
        var cols = Math.Clamp(request.Cols > 0 ? request.Cols : 80, 10, 500);
        var rows = Math.Clamp(request.Rows > 0 ? request.Rows : 24, 2, 200);

        // Validate explicit shell if supplied; otherwise simulate alpine/sh vs bash based on pod name.
        var shellCandidates = KubernetesPodExecService.ResolveShellCandidates(request.Shell);
        var resolvedShell = string.IsNullOrWhiteSpace(request.Shell) ||
                            string.Equals(request.Shell.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? (podName.Contains("nginx", StringComparison.OrdinalIgnoreCase) ||
               podName.Contains("coredns", StringComparison.OrdinalIgnoreCase)
                ? "/bin/sh"
                : "/bin/bash")
            : shellCandidates[0];

        var resolvedContainer = request.Container?.Trim();
        if (_kubernetesAdapter != null)
        {
            var pods = await _kubernetesAdapter.ListPodsAsync(namespaceName, ct: ct);
            var matchedPod = pods.FirstOrDefault(p => string.Equals(p.Name, podName, StringComparison.OrdinalIgnoreCase));
            if (matchedPod != null)
            {
                if (!string.IsNullOrWhiteSpace(resolvedContainer) &&
                    matchedPod.Containers is { Count: > 0 } &&
                    !matchedPod.Containers.Contains(resolvedContainer, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Container '{resolvedContainer}' was not found in demo pod '{namespaceName}/{podName}'. Available containers: {string.Join(", ", matchedPod.Containers)}.");
                }

                resolvedContainer ??= matchedPod.Containers?.FirstOrDefault();
            }
        }

        resolvedContainer ??= "app";

        var session = new DemoPodExecSession(
            clusterId,
            namespaceName,
            podName,
            resolvedContainer,
            resolvedShell,
            cols,
            rows,
            onOutput,
            onClosed);

        await session.WriteWelcomeAsync();
        return session;
    }

    private sealed class DemoPodExecSession(
        string clusterId,
        string namespaceName,
        string podName,
        string container,
        string resolvedShell,
        int initialCols,
        int initialRows,
        Func<string, Task> onOutput,
        Func<int, Task> onClosed) : IPodExecSession
    {
        private readonly SemaphoreSlim _inputLock = new(1, 1);
        private readonly StringBuilder _lineBuffer = new();
        private string _cwd = "/app";
        private int _cols = initialCols;
        private int _rows = initialRows;
        private bool _lastWasCarriageReturn;
        private int _closed;

        public string ClusterId { get; } = clusterId;
        public string Namespace { get; } = namespaceName;
        public string PodName { get; } = podName;
        public string? Container { get; } = container;
        public string ResolvedShell { get; } = resolvedShell;

        private string Prompt => $"root@{PodName}:{_cwd}# ";

        public async Task WriteWelcomeAsync()
        {
            var banner =
                $"\x1b[36m[DEMO MODE]\x1b[0m Connected to simulated pod \x1b[1m{Namespace}/{PodName}\x1b[0m " +
                $"(container: {Container}, shell: {ResolvedShell})\r\n" +
                "Type 'help' for available troubleshooting commands or 'exit' to close.\r\n\r\n" +
                Prompt;
            await onOutput(banner);
        }

        public async ValueTask SendInputAsync(string data, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(data) || Volatile.Read(ref _closed) != 0)
            {
                return;
            }

            await _inputLock.WaitAsync(ct);
            try
            {
                foreach (var ch in data)
                {
                    if (Volatile.Read(ref _closed) != 0)
                    {
                        return;
                    }

                    if (ch == '\n' && _lastWasCarriageReturn)
                    {
                        _lastWasCarriageReturn = false;
                        continue;
                    }

                    _lastWasCarriageReturn = ch == '\r';

                    switch (ch)
                    {
                        case '\u0003': // Ctrl+C
                            _lineBuffer.Clear();
                            await onOutput($"^C\r\n{Prompt}");
                            break;

                        case '\u0004': // Ctrl+D
                            if (_lineBuffer.Length == 0)
                            {
                                await onOutput("logout\r\n");
                                await CloseInternalAsync(0);
                                return;
                            }
                            break;

                        case '\u000c': // Ctrl+L
                            await onOutput($"\x1b[2J\x1b[H{Prompt}{_lineBuffer}");
                            break;

                        case '\u007f':
                        case '\b':
                            if (_lineBuffer.Length > 0)
                            {
                                _lineBuffer.Length--;
                                await onOutput("\b \b");
                            }
                            break;

                        case '\r':
                        case '\n':
                            var commandLine = _lineBuffer.ToString();
                            _lineBuffer.Clear();
                            await onOutput("\r\n");
                            var shouldExit = await ExecuteSimulatedCommandAsync(commandLine);
                            if (shouldExit)
                            {
                                await CloseInternalAsync(0);
                                return;
                            }
                            await onOutput(Prompt);
                            break;

                        default:
                            if (!char.IsControl(ch) || ch == '\t')
                            {
                                _lineBuffer.Append(ch);
                                await onOutput(ch.ToString());
                            }
                            break;
                    }
                }
            }
            finally
            {
                _inputLock.Release();
            }
        }

        public ValueTask ResizeAsync(int cols, int rows, CancellationToken ct = default)
        {
            _cols = Math.Clamp(cols > 0 ? cols : 80, 10, 500);
            _rows = Math.Clamp(rows > 0 ? rows : 24, 2, 200);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _closed, 1);
            _inputLock.Dispose();
            return ValueTask.CompletedTask;
        }

        private async Task CloseInternalAsync(int exitCode)
        {
            if (Interlocked.CompareExchange(ref _closed, 1, 0) == 0)
            {
                await onClosed(exitCode);
            }
        }

        private async Task<bool> ExecuteSimulatedCommandAsync(string rawCommand)
        {
            var trimmed = rawCommand.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return false;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0];
            var args = parts.Skip(1).ToArray();

            switch (cmd)
            {
                case "exit":
                case "logout":
                    await onOutput("logout\r\n");
                    return true;

                case "clear":
                    await onOutput("\x1b[2J\x1b[H");
                    return false;

                case "help":
                    await onOutput(
                        "Simulated pod shell commands:\r\n" +
                        "  ls, pwd, cd, cat, echo, env, printenv, stty\r\n" +
                        "  whoami, id, hostname, uname, uptime, ps, df, free\r\n" +
                        "  ip, nslookup, curl, clear, exit\r\n");
                    return false;

                case "pwd":
                    await onOutput($"{_cwd}\r\n");
                    return false;

                case "cd":
                    var target = args.Length == 0 || args[0] == "~" ? "/root" : args[0];
                    _cwd = target switch
                    {
                        ".." => _cwd == "/" ? "/" : "/",
                        "." => _cwd,
                        _ when target.StartsWith('/') => target.TrimEnd('/').Length == 0 ? "/" : target.TrimEnd('/'),
                        _ => _cwd == "/" ? $"/{target}" : $"{_cwd}/{target}"
                    };
                    return false;

                case "ls":
                case "ll":
                    var longFmt = cmd == "ll" || args.Any(a => a.Contains('l'));
                    if (longFmt)
                    {
                        await onOutput(
                            "drwxr-xr-x 1 root root 4096 Nov 14 01:00 .\r\n" +
                            "drwxr-xr-x 1 root root 4096 Nov 14 01:00 ..\r\n" +
                            "-rw-r--r-- 1 root root  512 Nov 14 01:00 appsettings.json\r\n" +
                            "drwxr-xr-x 2 root root 4096 Nov 14 01:00 config\r\n" +
                            "drwxr-xr-x 2 root root 4096 Nov 14 01:00 logs\r\n");
                    }
                    else
                    {
                        await onOutput("appsettings.json  config  logs\r\n");
                    }
                    return false;

                case "whoami":
                    await onOutput("root\r\n");
                    return false;

                case "id":
                    await onOutput("uid=0(root) gid=0(root) groups=0(root)\r\n");
                    return false;

                case "hostname":
                    await onOutput($"{PodName}\r\n");
                    return false;

                case "uname":
                    await onOutput(args.Any(a => a.Contains('a'))
                        ? $"Linux {PodName} 6.8.0-31-generic #31-Ubuntu SMP PREEMPT_DYNAMIC x86_64 GNU/Linux\r\n"
                        : "Linux\r\n");
                    return false;

                case "uptime":
                    await onOutput(" 01:55:00 up 7 days,  4:12,  1 user,  load average: 0.14, 0.19, 0.15\r\n");
                    return false;

                case "stty":
                    if (args.Length > 0 && args[0] == "size")
                    {
                        await onOutput($"{_rows} {_cols}\r\n");
                    }
                    else
                    {
                        await onOutput($"speed 38400 baud; rows {_rows}; columns {_cols};\r\n");
                    }
                    return false;

                case "env":
                case "printenv":
                    var envLines = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["HOSTNAME"] = PodName,
                        ["POD_NAME"] = PodName,
                        ["POD_NAMESPACE"] = Namespace,
                        ["CONTAINER_NAME"] = Container ?? "app",
                        ["CLUSTER_ID"] = ClusterId,
                        ["KUBERNETES_SERVICE_HOST"] = "10.96.0.1",
                        ["KUBERNETES_SERVICE_PORT"] = "443",
                        ["TERM"] = "xterm-256color",
                        ["SHELL"] = ResolvedShell,
                        ["COLUMNS"] = _cols.ToString(),
                        ["LINES"] = _rows.ToString(),
                        ["PWD"] = _cwd
                    };
                    if (args.Length > 0 && cmd == "printenv")
                    {
                        if (envLines.TryGetValue(args[0], out var val))
                        {
                            await onOutput($"{val}\r\n");
                        }
                        return false;
                    }
                    var sb = new StringBuilder();
                    foreach (var (k, v) in envLines)
                    {
                        sb.Append(k).Append('=').Append(v).Append("\r\n");
                    }
                    await onOutput(sb.ToString());
                    return false;

                case "ps":
                    await onOutput(
                        "PID   USER     TIME  COMMAND\r\n" +
                        $"    1 root      0:14 /usr/bin/{Container ?? "app"} --serve\r\n" +
                        $"   42 root      0:00 {ResolvedShell}\r\n" +
                        "   58 root      0:00 ps\r\n");
                    return false;

                case "df":
                    await onOutput(
                        "Filesystem      Size  Used Avail Use% Mounted on\r\n" +
                        "overlay         100G   18G   82G  18% /\r\n" +
                        "tmpfs            64M     0   64M   0% /dev\r\n");
                    return false;

                case "free":
                    await onOutput(
                        "               total        used        free      shared  buff/cache   available\r\n" +
                        "Mem:            8192        1840        4120          64        2232        6050\r\n");
                    return false;

                case "ip":
                case "ifconfig":
                    await onOutput(
                        "1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 state UNKNOWN\r\n" +
                        "    inet 127.0.0.1/8 scope host lo\r\n" +
                        "2: eth0@if18: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 state UP\r\n" +
                        "    inet 10.244.1.16/24 brd 10.244.1.255 scope global eth0\r\n");
                    return false;

                case "cat":
                    var file = args.FirstOrDefault() ?? "";
                    var content = file switch
                    {
                        "/etc/resolv.conf" =>
                            $"search {Namespace}.svc.cluster.local svc.cluster.local cluster.local\r\nnameserver 10.96.0.10\r\noptions ndots:5\r\n",
                        "/etc/hosts" =>
                            $"127.0.0.1\tlocalhost\r\n10.244.1.16\t{PodName}\r\n",
                        "/etc/os-release" =>
                            "PRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\r\nID=ubuntu\r\nVERSION_ID=\"24.04\"\r\n",
                        _ => $"cat: {file}: simulated file view (demo mode)\r\n"
                    };
                    await onOutput(content);
                    return false;

                case "nslookup":
                case "dig":
                    var host = args.FirstOrDefault() ?? "kubernetes.default.svc.cluster.local";
                    await onOutput(
                        "Server:\t\t10.96.0.10\r\n" +
                        "Address:\t10.96.0.10#53\r\n\r\n" +
                        $"Name:\t{host}\r\n" +
                        "Address: 10.96.0.1\r\n");
                    return false;

                case "curl":
                    await onOutput("{\"status\":\"ok\",\"demo\":true}\r\n");
                    return false;

                case "echo":
                    await onOutput($"{string.Join(' ', args)}\r\n");
                    return false;

                default:
                    var shellShort = Path.GetFileName(ResolvedShell);
                    await onOutput($"{shellShort}: {cmd}: command not found (demo shell - type 'help')\r\n");
                    return false;
            }
        }
    }
}

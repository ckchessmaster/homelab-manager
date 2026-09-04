using System.Text;
using Renci.SshNet;
using SshConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace ControlPlane.Api.Features.Adoption;

public interface ISshBootstrapper
{
    Task<string> ProbeArchitectureAsync(AdoptNodeRequest request, CancellationToken cancellationToken = default);
    Task UploadBinaryAsync(AdoptNodeRequest request, string localBinaryPath, string remotePath, CancellationToken cancellationToken = default);
    Task UploadTextAsync(AdoptNodeRequest request, string content, string remotePath, CancellationToken cancellationToken = default);
    Task<string> ExecuteRemoteCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default);
    Task<string> ExecutePrivilegedCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default);
}

public class SshBootstrapper : ISshBootstrapper
{
    private readonly ILogger<SshBootstrapper> _logger;

    public SshBootstrapper(ILogger<SshBootstrapper> logger)
    {
        _logger = logger;
    }

    private static SshConnectionInfo CreateConnectionInfo(AdoptNodeRequest request)
    {
        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(request.PrivateKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(request.PrivateKey);
            using var keyStream = new MemoryStream(keyBytes);
            var keyFile = new PrivateKeyFile(keyStream);
            authMethods.Add(new PrivateKeyAuthenticationMethod(request.Username, keyFile));
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(request.Username, request.Password));
        }

        if (authMethods.Count == 0)
        {
            throw new ArgumentException("Either Password or PrivateKey must be provided for SSH authentication.");
        }

        return new SshConnectionInfo(request.TargetHost, request.Port, request.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public Task<string> ProbeArchitectureAsync(AdoptNodeRequest request, CancellationToken cancellationToken = default)
    {
        var connInfo = CreateConnectionInfo(request);
        using var client = new SshClient(connInfo);
        client.Connect();
        try
        {
            var cmd = client.RunCommand("uname -m");
            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Architecture probe failed with exit status {cmd.ExitStatus}: {cmd.Error}");
            }
            return Task.FromResult(cmd.Result.Trim());
        }
        finally
        {
            client.Disconnect();
        }
    }

    public Task UploadBinaryAsync(AdoptNodeRequest request, string localBinaryPath, string remotePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localBinaryPath))
        {
            throw new FileNotFoundException($"Agent binary not found at '{localBinaryPath}'");
        }

        var connInfo = CreateConnectionInfo(request);
        using var sftp = new SftpClient(connInfo);
        sftp.Connect();
        try
        {
            using var fileStream = File.OpenRead(localBinaryPath);
            sftp.UploadFile(fileStream, remotePath, true);
        }
        finally
        {
            sftp.Disconnect();
        }

        // Chmod +x via ssh
        using var ssh = new SshClient(connInfo);
        ssh.Connect();
        try
        {
            ssh.RunCommand($"chmod +x '{remotePath}'");
        }
        finally
        {
            ssh.Disconnect();
        }

        return Task.CompletedTask;
    }

    public Task UploadTextAsync(AdoptNodeRequest request, string content, string remotePath, CancellationToken cancellationToken = default)
    {
        var connInfo = CreateConnectionInfo(request);
        using var sftp = new SftpClient(connInfo);
        sftp.Connect();
        try
        {
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            sftp.UploadFile(memoryStream, remotePath, true);
        }
        finally
        {
            sftp.Disconnect();
        }

        return Task.CompletedTask;
    }

    public Task<string> ExecuteRemoteCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default)
    {
        var connInfo = CreateConnectionInfo(request);
        using var client = new SshClient(connInfo);
        client.Connect();
        try
        {
            var cmd = client.RunCommand(command);
            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Command '{command}' failed with exit status {cmd.ExitStatus}: {cmd.Error}");
            }
            return Task.FromResult(cmd.Result);
        }
        finally
        {
            client.Disconnect();
        }
    }

    public Task<string> ExecutePrivilegedCommandAsync(AdoptNodeRequest request, string command, CancellationToken cancellationToken = default)
    {
        var scriptName = $"/tmp/.cp_exec_{Guid.NewGuid():N}.sh";
        var b64Cmd = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        string fullCommand;

        if (request.Username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            fullCommand = $"sh -c \"trap 'rm -f {scriptName}' EXIT; echo '{b64Cmd}' | base64 -d > {scriptName} && chmod +x {scriptName} && {scriptName}\"";
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            var b64Pass = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Password));
            fullCommand = $"""
                sh -c "trap 'rm -f {scriptName}' EXIT
                echo '{b64Cmd}' | base64 -d > {scriptName} && chmod +x {scriptName} || exit 1
                if [ \"$(id -u)\" -eq 0 ]; then
                    {scriptName}
                elif sudo -n true 2>/dev/null; then
                    sudo -n {scriptName}
                else
                    echo '{b64Pass}' | base64 -d | sudo -S -p '' {scriptName}
                fi"
                """;
        }
        else
        {
            fullCommand = $"""
                sh -c "trap 'rm -f {scriptName}' EXIT
                echo '{b64Cmd}' | base64 -d > {scriptName} && chmod +x {scriptName} || exit 1
                if [ \"$(id -u)\" -eq 0 ]; then
                    {scriptName}
                elif sudo -n true 2>/dev/null; then
                    sudo -n {scriptName}
                else
                    echo \"User '{request.Username}' is not root and sudo requires a password. Please provide a password or configure NOPASSWD in sudoers.\" >&2
                    exit 1
                fi"
                """;
        }

        var connInfo = CreateConnectionInfo(request);
        using var client = new SshClient(connInfo);
        client.Connect();
        try
        {
            var cmd = client.RunCommand(fullCommand);
            if (cmd.ExitStatus != 0)
            {
                var errorMsg = string.IsNullOrWhiteSpace(cmd.Error) ? cmd.Result : cmd.Error;
                throw new InvalidOperationException($"Privileged command '{command}' failed with exit status {cmd.ExitStatus}: {errorMsg?.Trim()}");
            }
            return Task.FromResult(cmd.Result);
        }
        finally
        {
            client.Disconnect();
        }
    }
}

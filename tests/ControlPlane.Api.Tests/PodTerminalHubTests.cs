using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Security.Tokens;
using ControlPlane.Api.Features.SystemLogs;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ControlPlane.Api.Tests;

public class PodTerminalHubTests
{
    private const string TestSigningKey = "PodTerminalHubTestSigningSecretKey_0123456789!";
    private const string TestApiKey = "pod-terminal-static-key-777";

    private sealed class FakePodExecSession : IPodExecSession
    {
        private readonly Func<string, Task> _onOutput;
        private readonly Func<int, Task> _onClosed;
        private readonly ConcurrentQueue<string> _inputs = new();
        private readonly ConcurrentQueue<(int Cols, int Rows)> _resizes = new();
        private readonly TaskCompletionSource _disposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public FakePodExecSession(
            PodExecRequest request,
            Func<string, Task> onOutput,
            Func<int, Task> onClosed)
        {
            ClusterId = string.IsNullOrWhiteSpace(request.ClusterId) ? "default" : request.ClusterId;
            Namespace = request.Namespace;
            PodName = request.PodName;
            Container = request.Container ?? "app";
            ResolvedShell = request.Shell ?? "/bin/bash";
            _onOutput = onOutput;
            _onClosed = onClosed;
        }

        public string ClusterId { get; }
        public string Namespace { get; }
        public string PodName { get; }
        public string? Container { get; }
        public string ResolvedShell { get; }

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;
        public Task DisposedTask => _disposedTcs.Task;
        public IReadOnlyCollection<string> Inputs => _inputs.ToArray();
        public IReadOnlyCollection<(int Cols, int Rows)> Resizes => _resizes.ToArray();

        public async ValueTask SendInputAsync(string data, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _inputs.Enqueue(data);
            await _onOutput($"echo:{data}");
        }

        public ValueTask ResizeAsync(int cols, int rows, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _resizes.Enqueue((cols, rows));
            return ValueTask.CompletedTask;
        }

        public Task EmitServerOutputAsync(string text) => _onOutput(text);

        public Task TriggerRemoteExitAsync(int exitCode) => _onClosed(exitCode);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _disposedTcs.TrySetResult();
                // Simulate underlying socket EOF firing onClosed during disposal
                await _onClosed(137);
            }
        }
    }

    private sealed class TrackingPodExecService : IPodExecService
    {
        private readonly ConcurrentQueue<FakePodExecSession> _sessions = new();

        public Func<PodExecRequest, Exception?>? FailureInjector { get; set; }
        public IReadOnlyList<FakePodExecSession> CreatedSessions => _sessions.ToArray();

        public async Task<IPodExecSession> AttachAsync(
            PodExecRequest request,
            Func<string, Task> onOutput,
            Func<int, Task> onClosed,
            CancellationToken ct = default)
        {
            var injected = FailureInjector?.Invoke(request);
            if (injected != null)
            {
                throw injected;
            }

            var session = new FakePodExecSession(request, onOutput, onClosed);
            _sessions.Enqueue(session);

            // Emit initial banner during AttachAsync before Hub invocation returns
            await onOutput($"connected:{session.ClusterId}/{session.Namespace}/{session.PodName}# ");
            return session;
        }
    }

    private sealed class PodTerminalTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-podterm-{Guid.NewGuid():N}.db");
        private readonly IPodExecService? _overridePodExecService;
        private readonly bool _demoMode;
        private readonly bool _podExecRequireAdmin;

        public PodTerminalTestAppFactory(
            IPodExecService? overridePodExecService = null,
            bool demoMode = false,
            bool podExecRequireAdmin = false)
        {
            _overridePodExecService = overridePodExecService;
            _demoMode = demoMode;
            _podExecRequireAdmin = podExecRequireAdmin;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", TestApiKey);
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseSetting("DEMO_MODE", _demoMode ? "true" : "false");
            builder.UseSetting("POD_EXEC_REQUIRE_ADMIN", _podExecRequireAdmin ? "true" : "false");
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ControlPlaneDbContext>));
                if (dbDescriptor != null)
                {
                    services.Remove(dbDescriptor);
                }

                services.AddDbContext<ControlPlaneDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_tempDbFile}")
                        .UseSnakeCaseNamingConvention();
                });

                services.PostConfigure<JwtBearerOptions>(AuthConstants.JwtBearerScheme, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false
                    };
                });

                if (_overridePodExecService != null)
                {
                    services.RemoveAll<IPodExecService>();
                    services.AddSingleton(_overridePodExecService);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_tempDbFile))
            {
                try { File.Delete(_tempDbFile); } catch { }
            }
        }
    }

    private static string GenerateJwt(string username, params string[] groups)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Sub, username),
            new("preferred_username", username),
            new(JwtRegisteredClaimNames.Iss, "https://issuer.example.com"),
            new(JwtRegisteredClaimNames.Aud, "controlplane")
        };

        if (groups.Length > 0)
        {
            var json = "[\"" + string.Join("\",\"", groups) + "\"]";
            claims.Add(new Claim(AuthConstants.DefaultRoleClaimType, json));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    private static HubConnection BuildHubConnection(
        PodTerminalTestAppFactory factory,
        string? accessToken = null,
        string? apiKeyHeader = null,
        bool useWebSockets = true,
        bool skipNegotiation = false)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/pod-terminal"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (useWebSockets)
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = skipNegotiation;
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var wsClient = factory.Server.CreateWebSocketClient();
                        if (apiKeyHeader != null)
                        {
                            var headerValue = apiKeyHeader;
                            wsClient.ConfigureRequest = req => req.Headers["X-ControlPlane-Key"] = headerValue;
                        }

                        var targetUri = context.Uri;
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            // Match browser @microsoft/signalr WebSocketTransport behavior:
                            // browsers cannot set Authorization headers on WebSocket upgrades and pass ?access_token= in the query string.
                            var separator = string.IsNullOrEmpty(targetUri.Query) ? "?" : "&";
                            targetUri = new Uri($"{targetUri}{separator}access_token={Uri.EscapeDataString(accessToken)}");
                        }

                        return await wsClient.ConnectAsync(targetUri, cancellationToken);
                    };
                }

                if (accessToken != null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }

                if (apiKeyHeader != null)
                {
                    options.Headers["X-ControlPlane-Key"] = apiKeyHeader;
                }
            })
            .Build();
    }

    [Fact]
    public async Task Attach_Input_Resize_And_RemoteExit_StreamsViaHubContext_AndAuditsSession()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        var operatorJwt = GenerateJwt("ops-engineer", "operator");
        await using var hub = BuildHubConnection(factory, accessToken: operatorJwt);

        var outputs = new ConcurrentQueue<string>();
        var attachedTcs = new TaskCompletionSource<PodTerminalAttachedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var echoedSecretTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundPushTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.On<PodTerminalAttachedEvent>("Attached", evt => attachedTcs.TrySetResult(evt));
        hub.On<string>("Output", text =>
        {
            outputs.Enqueue(text);
            if (text.Contains("echo:export DB_PASS=TopSecretValue99!", StringComparison.Ordinal))
            {
                echoedSecretTcs.TrySetResult(true);
            }
            if (text.Contains("async-daemon-log-line", StringComparison.Ordinal))
            {
                backgroundPushTcs.TrySetResult(true);
            }
        });
        hub.On<int>("Closed", code => closedTcs.TrySetResult(code));

        await hub.StartAsync();

        var attachResult = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "production",
            PodName: "api-gateway-7b9d4",
            ClusterId: "edge-k3s",
            Container: "gateway",
            Shell: "/bin/sh",
            Cols: 120,
            Rows: 36));

        Assert.True(attachResult.Succeeded);
        Assert.Equal("edge-k3s", attachResult.ClusterId);
        Assert.Equal("production", attachResult.Namespace);
        Assert.Equal("api-gateway-7b9d4", attachResult.PodName);
        Assert.Equal("gateway", attachResult.Container);
        Assert.Equal("/bin/sh", attachResult.ResolvedShell);

        var attachedEvent = await attachedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(attachResult.SessionId, attachedEvent.SessionId);
        Assert.Equal(120, attachedEvent.Cols);
        Assert.Equal(36, attachedEvent.Rows);

        var sessionRegistry = factory.Services.GetRequiredService<PodTerminalSessionRegistry>();
        Assert.Equal(1, sessionRegistry.ActiveSessionCount);
        var createdSession = Assert.Single(execService.CreatedSessions);

        // Stream background output AFTER Hub.Attach invocation scope has already completed & disposed
        await createdSession.EmitServerOutputAsync("async-daemon-log-line\r\n");
        await backgroundPushTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Send interactive input & resize across separate transient Hub invocations
        await hub.InvokeAsync("Input", "export DB_PASS=TopSecretValue99!\n");
        await echoedSecretTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("export DB_PASS=TopSecretValue99!\n", createdSession.Inputs);

        await hub.InvokeAsync("Resize", 140, 48);
        Assert.Contains((140, 48), createdSession.Resizes);

        // Trigger remote shell termination and verify Closed event + registry cleanup + audit trail
        await createdSession.TriggerRemoteExitAsync(0);
        var exitCode = await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, exitCode);
        Assert.Equal(0, sessionRegistry.ActiveSessionCount);

        var auditLogger = factory.Services.GetRequiredService<IPodExecAuditLogger>();
        var auditEntries = auditLogger.GetRecentEntries();
        var startedAudit = Assert.Single(auditEntries, e => e.Action == PodExecAuditActions.SessionStarted && e.SessionId == attachResult.SessionId);
        var endedAudit = Assert.Single(auditEntries, e => e.Action == PodExecAuditActions.SessionEnded && e.SessionId == attachResult.SessionId);

        Assert.Equal("ops-engineer", startedAudit.PrincipalName);
        Assert.Contains(AuthConstants.RoleOperator, startedAudit.Roles);
        Assert.Equal("edge-k3s", startedAudit.ClusterId);
        Assert.Equal("production", startedAudit.Namespace);
        Assert.Equal("api-gateway-7b9d4", startedAudit.PodName);
        Assert.Equal("gateway", startedAudit.Container);
        Assert.Equal("/bin/sh", startedAudit.Shell);

        Assert.Equal("RemoteExited", endedAudit.Reason);
        Assert.Equal(0, endedAudit.ExitCode);
        Assert.NotNull(endedAudit.Duration);
        Assert.True(endedAudit.Duration >= TimeSpan.Zero);

        // Verify structured audit events also flow into ISystemLogBuffer and NEVER contain stdin/stdout secrets
        var sysLogBuffer = factory.Services.GetRequiredService<ISystemLogBuffer>();
        var podExecSysLogs = sysLogBuffer.Query(category: nameof(PodExecAuditLogger), limit: 50).Logs;
        Assert.Contains(podExecSysLogs, l => l.EventName == "PodExecSessionStarted" && l.Message.Contains("ops-engineer"));
        Assert.Contains(podExecSysLogs, l => l.EventName == "PodExecSessionEnded" && l.Message.Contains("RemoteExited"));

        var allSysLogs = sysLogBuffer.Query(limit: 500).Logs;
        Assert.DoesNotContain(allSysLogs, l => l.Message.Contains("TopSecretValue99!", StringComparison.Ordinal));

        await hub.StopAsync();
    }

    [Fact]
    public async Task OnDisconnectedAsync_DisposesActiveSession_AndRecordsClientDisconnectedAudit()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        var adminJwt = GenerateJwt("cluster-admin", "admin");
        var hub = BuildHubConnection(factory, accessToken: adminJwt);
        await hub.StartAsync();

        var result = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "kube-system",
            PodName: "coredns-5d78c9869d-abcde"));

        Assert.True(result.Succeeded);

        var sessionRegistry = factory.Services.GetRequiredService<PodTerminalSessionRegistry>();
        Assert.Equal(1, sessionRegistry.ActiveSessionCount);

        var session = Assert.Single(execService.CreatedSessions);
        Assert.False(session.IsDisposed);

        // Disconnect client abruptly without exiting remote shell
        await hub.StopAsync();
        await hub.DisposeAsync();

        await session.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.IsDisposed);
        Assert.Equal(0, sessionRegistry.ActiveSessionCount);

        var auditLogger = factory.Services.GetRequiredService<IPodExecAuditLogger>();
        var endedAudit = Assert.Single(
            auditLogger.GetRecentEntries(),
            e => e.Action == PodExecAuditActions.SessionEnded && e.SessionId == result.SessionId);
        Assert.StartsWith("ClientDisconnected", endedAudit.Reason);
    }

    [Fact]
    public async Task Reattach_And_Detach_DisposePreviousSession_WithoutSpuriousClosedEvent()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        var operatorJwt = GenerateJwt("reattach-operator", "operator");
        await using var hub = BuildHubConnection(factory, accessToken: operatorJwt);

        var closedEvents = new ConcurrentQueue<int>();
        var secondPodEchoTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.On<int>("Closed", code => closedEvents.Enqueue(code));
        hub.On<string>("Output", text =>
        {
            if (text.Contains("echo:ping-second\n", StringComparison.Ordinal))
            {
                secondPodEchoTcs.TrySetResult(true);
            }
        });

        await hub.StartAsync();

        var firstAttach = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "default",
            PodName: "pod-first",
            Container: "main"));
        Assert.True(firstAttach.Succeeded);

        var secondAttach = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "default",
            PodName: "pod-second",
            Container: "sidecar"));
        Assert.True(secondAttach.Succeeded);

        Assert.Equal(2, execService.CreatedSessions.Count);
        var firstSession = execService.CreatedSessions[0];
        var secondSession = execService.CreatedSessions[1];

        await firstSession.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstSession.IsDisposed);
        Assert.False(secondSession.IsDisposed);

        // Disposing the superseded first session must NOT fire Closed on the active SignalR client
        Assert.Empty(closedEvents);

        await hub.InvokeAsync("Input", "ping-second\n");
        await secondPodEchoTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(firstSession.Inputs);
        Assert.Contains("ping-second\n", secondSession.Inputs);

        // Explicit Detach cleans up the second session while keeping the SignalR connection alive
        await hub.InvokeAsync("Detach");
        await secondSession.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));

        var sessionRegistry = factory.Services.GetRequiredService<PodTerminalSessionRegistry>();
        Assert.Equal(0, sessionRegistry.ActiveSessionCount);

        var audits = factory.Services.GetRequiredService<IPodExecAuditLogger>().GetRecentEntries();
        Assert.Contains(audits, a => a.SessionId == firstAttach.SessionId && a.Action == PodExecAuditActions.SessionEnded && a.Reason == "Reattached");
        Assert.Contains(audits, a => a.SessionId == secondAttach.SessionId && a.Action == PodExecAuditActions.SessionEnded && a.Reason == "ClientDetached");
    }

    [Fact]
    public async Task Attach_ValidationAndAdapterFailures_SendErrorAndAnsiOutput_AndAuditFailure()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        var operatorJwt = GenerateJwt("troubleshooter", "operator");
        await using var hub = BuildHubConnection(factory, accessToken: operatorJwt);

        var errors = new ConcurrentQueue<string>();
        var outputs = new ConcurrentQueue<string>();
        var errorReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.On<string>("Error", err =>
        {
            errors.Enqueue(err);
            errorReceivedTcs.TrySetResult(err);
        });
        hub.On<string>("Output", text => outputs.Enqueue(text));

        await hub.StartAsync();

        // 1. Missing podName validation error
        var missingPodResult = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "default",
            PodName: ""));
        Assert.False(missingPodResult.Succeeded);
        Assert.NotNull(missingPodResult.Error);

        var firstError = await errorReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("required", firstError, StringComparison.OrdinalIgnoreCase);

        // 2. Service exception (e.g. non-running pod / K8s API error)
        errorReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        execService.FailureInjector = _ => new InvalidOperationException("Pod 'default/crashloop-pod' is in phase 'Failed', not Running.");

        var failedResult = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "default",
            PodName: "crashloop-pod"));
        Assert.False(failedResult.Succeeded);
        Assert.Contains("not Running", failedResult.Error);

        var secondError = await errorReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("not Running", secondError);
        Assert.Contains(outputs, o => o.Contains("\x1b[31m") && o.Contains("crashloop-pod"));

        var audits = factory.Services.GetRequiredService<IPodExecAuditLogger>().GetRecentEntries();
        Assert.Equal(2, audits.Count(a => a.Action == PodExecAuditActions.AttachFailed));

        var sysLogs = factory.Services.GetRequiredService<ISystemLogBuffer>().Query(category: nameof(PodExecAuditLogger)).Logs;
        Assert.Contains(sysLogs, l => l.EventName == "PodExecAttachFailed" && l.Message.Contains("crashloop-pod"));
    }

    [Fact]
    public async Task RbacEnforcement_RejectsAnonymousAndViewer_AllowsOperatorAndAdmin_AndSupportsAdminOnlyPolicy()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        // 1. Anonymous caller -> 401 Unauthorized
        await using (var anonHub = BuildHubConnection(factory, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => anonHub.StartAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        }

        // 2. Viewer role -> 403 Forbidden
        var viewerJwt = GenerateJwt("readonly-viewer", "viewer");
        await using (var viewerHub = BuildHubConnection(factory, accessToken: viewerJwt, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => viewerHub.StartAsync());
            Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // 3. Operator role -> Allowed
        var operatorJwt = GenerateJwt("sre-operator", "operator");
        await using (var operatorHub = BuildHubConnection(factory, accessToken: operatorJwt))
        {
            await operatorHub.StartAsync();
            var res = await operatorHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "web-0"));
            Assert.True(res.Succeeded);
        }

        // 4. Admin role -> Allowed
        var adminJwt = GenerateJwt("platform-admin", "admin");
        await using (var adminHub = BuildHubConnection(factory, accessToken: adminJwt))
        {
            await adminHub.StartAsync();
            var res = await adminHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "web-1"));
            Assert.True(res.Succeeded);
        }

        // 5. Strict POD_EXEC_REQUIRE_ADMIN=true mode -> Operator rejected (403), Admin allowed
        using var adminOnlyFactory = new PodTerminalTestAppFactory(new TrackingPodExecService(), podExecRequireAdmin: true);
        await using (var operatorDeniedHub = BuildHubConnection(adminOnlyFactory, accessToken: operatorJwt, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => operatorDeniedHub.StartAsync());
            Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        await using (var adminAllowedHub = BuildHubConnection(adminOnlyFactory, accessToken: adminJwt))
        {
            await adminAllowedHub.StartAsync();
            var res = await adminAllowedHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "secure-pod"));
            Assert.True(res.Succeeded);
        }
    }

    [Fact]
    public async Task WebSocketAccessTokenParity_SupportsJwt_StaticApiKey_AndPersonalAccessTokens_OnDirectWebSockets()
    {
        var execService = new TrackingPodExecService();
        using var factory = new PodTerminalTestAppFactory(execService);

        string operatorPat;
        string viewerPat;
        using (var scope = factory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
            operatorPat = (await tokenService.CreateTokenAsync(new CreateApiTokenRequest("ws-operator-pat", AuthConstants.RoleOperator, 7))).Token;
            viewerPat = (await tokenService.CreateTokenAsync(new CreateApiTokenRequest("ws-viewer-pat", AuthConstants.RoleViewer, 7))).Token;
        }

        // 1. Direct WebSocket (SkipNegotiation = true, ?access_token=<staticApiKey>, zero HTTP headers)
        await using (var staticKeyWsHub = BuildHubConnection(factory, accessToken: TestApiKey, useWebSockets: true, skipNegotiation: true))
        {
            await staticKeyWsHub.StartAsync();
            var res = await staticKeyWsHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "static-key-pod"));
            Assert.True(res.Succeeded);
        }

        // 2. Negotiated + Direct WebSocket with Operator Personal Access Token (?access_token=cp_pat_...)
        await using (var operatorPatWsHub = BuildHubConnection(factory, accessToken: operatorPat, useWebSockets: true, skipNegotiation: true))
        {
            await operatorPatWsHub.StartAsync();
            var res = await operatorPatWsHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "pat-operator-pod"));
            Assert.True(res.Succeeded);
        }

        // 3. Direct WebSocket with OIDC JWT (?access_token=<jwt>)
        var operatorJwt = GenerateJwt("ws-jwt-operator", "operator");
        await using (var jwtWsHub = BuildHubConnection(factory, accessToken: operatorJwt, useWebSockets: true, skipNegotiation: true))
        {
            await jwtWsHub.StartAsync();
            var res = await jwtWsHub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
                Namespace: "default",
                PodName: "jwt-operator-pod"));
            Assert.True(res.Succeeded);
        }

        // 4. Viewer Personal Access Token rejected (403 on negotiate and rejected on direct WebSocket)
        await using (var viewerPatNegotiateHub = BuildHubConnection(factory, accessToken: viewerPat, useWebSockets: false))
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => viewerPatNegotiateHub.StartAsync());
            Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        await using (var viewerPatDirectWsHub = BuildHubConnection(factory, accessToken: viewerPat, useWebSockets: true, skipNegotiation: true))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => viewerPatDirectWsHub.StartAsync());
        }

        // 5. Invalid token on direct WebSocket rejected
        await using (var invalidWsHub = BuildHubConnection(factory, accessToken: "invalid-token-value", useWebSockets: true, skipNegotiation: true))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => invalidWsHub.StartAsync());
        }

        // 6. Verify query-string access_token is NOT accepted on non-hub REST endpoints
        using var httpClient = factory.CreateClient();
        var restLeakCheck = await httpClient.GetAsync($"/api/v1/hosts?access_token={Uri.EscapeDataString(TestApiKey)}");
        Assert.Equal(HttpStatusCode.Unauthorized, restLeakCheck.StatusCode);

        // 7. Verify audit entries captured distinct auth_method values across WebSocket connections
        var audits = factory.Services.GetRequiredService<IPodExecAuditLogger>().GetRecentEntries();
        Assert.Contains(audits, a => a.PodName == "static-key-pod" && a.AuthMethod == "api_key" && a.PrincipalName == "ApiKeyUser");
        Assert.Contains(audits, a => a.PodName == "pat-operator-pod" && a.AuthMethod == "personal_access_token" && a.PrincipalName == "ws-operator-pat");
        Assert.Contains(audits, a => a.PodName == "jwt-operator-pod" && a.PrincipalName == "ws-jwt-operator");
    }

    [Fact]
    public async Task DemoMode_PodTerminalHub_StreamsInteractiveSimulatedShellOverWebSocket()
    {
        using var factory = new PodTerminalTestAppFactory(demoMode: true);
        await using var hub = BuildHubConnection(factory, useWebSockets: true, skipNegotiation: true);

        var outputBuilder = new StringBuilder();
        var outputLock = new Lock();
        var whoamiReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.On<string>("Output", chunk =>
        {
            lock (outputLock)
            {
                outputBuilder.Append(chunk);
                if (outputBuilder.ToString().Contains("root", StringComparison.OrdinalIgnoreCase))
                {
                    whoamiReceived.TrySetResult(true);
                }
            }
        });
        hub.On<int>("Closed", code => closedTcs.TrySetResult(code));

        await hub.StartAsync();

        var attachRes = await hub.InvokeAsync<PodTerminalAttachResult>("Attach", new PodTerminalAttachRequest(
            Namespace: "default",
            PodName: "nginx-ingress-demo",
            Cols: 100,
            Rows: 30));

        Assert.True(attachRes.Succeeded);
        Assert.Equal("/bin/sh", attachRes.ResolvedShell);

        await hub.InvokeAsync("Input", "whoami\n");
        await whoamiReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await hub.InvokeAsync("Resize", 132, 42);
        await hub.InvokeAsync("Input", "exit\n");

        var closeCode = await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, closeCode);
    }
}

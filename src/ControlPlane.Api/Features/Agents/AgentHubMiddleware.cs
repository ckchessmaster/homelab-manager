using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Jobs;
using ControlPlane.Api.Security;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Agents;

public class AgentHubMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AgentConnectionManager _connectionManager;
    private readonly AgentHeartbeatHandler _heartbeatHandler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ApiKeyAuthenticationOptions> _apiKeyOptions;
    private readonly ILogger<AgentHubMiddleware> _logger;
    private readonly IStepLogConsumer? _logConsumer;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AgentHubMiddleware(
        RequestDelegate next,
        AgentConnectionManager connectionManager,
        AgentHeartbeatHandler heartbeatHandler,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ApiKeyAuthenticationOptions> apiKeyOptions,
        ILogger<AgentHubMiddleware> logger,
        IStepLogConsumer? logConsumer = null)
    {
        _next = next;
        _connectionManager = connectionManager;
        _heartbeatHandler = heartbeatHandler;
        _scopeFactory = scopeFactory;
        _apiKeyOptions = apiKeyOptions;
        _logger = logger;
        _logConsumer = logConsumer;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals("/agent-hub", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection expected at /agent-hub.");
            return;
        }

        var token = ExtractToken(context);
        var host = await AuthenticateAgentAsync(context, token);
        if (host == null)
        {
            _logger.LogWarning("Agent connection rejected: Node identifier '{NodeId}' or token did not match any registered host.", GetNodeId(context));
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid agent token or node identifier.");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var nodeId = GetNodeId(context) ?? host.Id.ToString();
        var inboundHost = context.Request.Host.Value;
        var inboundScheme = context.Request.Scheme;

        _connectionManager.Register(host.Id, nodeId, webSocket, inboundHost, inboundScheme);
        _logger.LogInformation("Agent connected for host {Hostname} ({HostId})", host.Hostname, host.Id);

        try
        {
            var buffer = new byte[1024 * 32];
            var messageAccumulator = new StringBuilder();

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        if (webSocket.State == WebSocketState.CloseReceived)
                        {
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageAccumulator.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        var rawJson = messageAccumulator.ToString();
                        messageAccumulator.Clear();
                        await ProcessIncomingMessageAsync(host.Id, rawJson, context.RequestAborted);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Request aborted or shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling agent WebSocket session for host {HostId}", host.Id);
        }
        finally
        {
            _connectionManager.Unregister(host.Id, webSocket);
            _logger.LogInformation("Agent disconnected for host {Hostname} ({HostId})", host.Hostname, host.Id);
        }
    }

    private string? ExtractToken(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authStr = authHeader.ToString();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authStr.Substring("Bearer ".Length).Trim();
            }
        }

        if (context.Request.Query.TryGetValue("token", out var queryToken))
        {
            return queryToken.ToString();
        }

        return null;
    }

    private bool IsValidToken(string? token)
    {
        var expectedApiKey = _apiKeyOptions.CurrentValue.ApiKey;
        return _apiKeyOptions.CurrentValue.BypassAuth ||
               (!string.IsNullOrEmpty(expectedApiKey) && token == expectedApiKey);
    }

    private async Task<Storage.Entities.Host?> AuthenticateAgentAsync(HttpContext context, string? token)
    {
        var nodeId = GetNodeId(context);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        // 1. Check if token matches a Host ID directly
        if (Guid.TryParse(token, out var parsedHostId))
        {
            var matchedHost = await db.Hosts.FirstOrDefaultAsync(h => h.Id == parsedHostId);
            if (matchedHost != null)
            {
                return matchedHost;
            }
        }

        // 2. Check if nodeId matches a Host ID
        if (Guid.TryParse(nodeId, out var parsedNodeHostId))
        {
            var matchedHost = await db.Hosts.FirstOrDefaultAsync(h => h.Id == parsedNodeHostId);
            if (matchedHost != null)
            {
                // Verify token if configured
                var expectedApiKey = _apiKeyOptions.CurrentValue.ApiKey;
                if (_apiKeyOptions.CurrentValue.BypassAuth || string.IsNullOrEmpty(expectedApiKey) || token == expectedApiKey || token == parsedNodeHostId.ToString())
                {
                    return matchedHost;
                }
            }
        }

        // 3. Check if X-ControlPlane-Hostname matches Hostname
        if (context.Request.Headers.TryGetValue("X-ControlPlane-Hostname", out var headerHostname) &&
            !string.IsNullOrWhiteSpace(headerHostname))
        {
            var hName = headerHostname.ToString().Trim().ToLowerInvariant();
            var matchedHost = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname.ToLower() == hName);
            if (matchedHost != null && IsValidToken(token))
            {
                return matchedHost;
            }
        }

        // 4. Check if nodeId matches Hostname
        if (!string.IsNullOrEmpty(nodeId))
        {
            var matchedHost = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname.ToLower() == nodeId.ToLower());
            if (matchedHost != null)
            {
                var expectedApiKey = _apiKeyOptions.CurrentValue.ApiKey;
                if (_apiKeyOptions.CurrentValue.BypassAuth || string.IsNullOrEmpty(expectedApiKey) || token == expectedApiKey)
                {
                    return matchedHost;
                }
            }
        }

        // 4. Check if nodeId matches Host IP address
        if (!string.IsNullOrEmpty(nodeId))
        {
            var matchedHost = await db.Hosts.FirstOrDefaultAsync(h => h.IpAddress.ToLower() == nodeId.ToLower());
            if (matchedHost != null)
            {
                var expectedApiKey = _apiKeyOptions.CurrentValue.ApiKey;
                if (_apiKeyOptions.CurrentValue.BypassAuth || string.IsNullOrEmpty(expectedApiKey) || token == expectedApiKey)
                {
                    return matchedHost;
                }
            }
        }

        // 5. In dev bypass mode with an API key match, map by query hostId
        var configuredKey = _apiKeyOptions.CurrentValue.ApiKey;
        if (_apiKeyOptions.CurrentValue.BypassAuth || (!string.IsNullOrEmpty(configuredKey) && token == configuredKey))
        {
            if (context.Request.Query.TryGetValue("hostId", out var qHostId) && Guid.TryParse(qHostId, out var targetId))
            {
                var targetHost = await db.Hosts.FirstOrDefaultAsync(h => h.Id == targetId);
                if (targetHost != null) return targetHost;
            }
        }

        _logger.LogWarning("Agent connection rejected: Node identifier '{NodeId}' or token did not match any registered host.", nodeId);
        return null;
    }

    private static string? GetNodeId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-ControlPlane-Node-Id", out var headerNodeId))
        {
            return headerNodeId.ToString();
        }
        if (context.Request.Query.TryGetValue("nodeId", out var queryNodeId))
        {
            return queryNodeId.ToString();
        }
        return null;
    }

    private async Task ProcessIncomingMessageAsync(Guid hostId, string rawJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var type = typeProp.GetString();
            if (string.Equals(type, "HEARTBEAT", StringComparison.OrdinalIgnoreCase))
            {
                var heartbeat = JsonSerializer.Deserialize<AgentHeartbeatMessage>(rawJson, JsonOptions);
                if (heartbeat != null)
                {
                    await _heartbeatHandler.HandleHeartbeatAsync(hostId, heartbeat, ct);
                }
            }
            else if (string.Equals(type, "FRAME", StringComparison.OrdinalIgnoreCase) && _logConsumer != null)
            {
                var frameEnvelope = JsonSerializer.Deserialize<AgentFrameMessage>(rawJson, JsonOptions);
                if (frameEnvelope != null && frameEnvelope.Frame != null)
                {
                    await _logConsumer.ConsumeFrameAsync(hostId, frameEnvelope.Frame, ct);
                }
            }
            else if (string.Equals(type, "REBOOT_COMMENCING", StringComparison.OrdinalIgnoreCase))
            {
                string? jobId = null;
                if (doc.RootElement.TryGetProperty("jobId", out var jProp))
                {
                    jobId = jProp.GetString();
                }
                _connectionManager.NotifyRebootCommencing(hostId, jobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize agent message for host {HostId}: {RawJson}", hostId, rawJson);
        }
    }
}

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

/// <summary>
/// Parameters for attaching an interactive terminal session to a Kubernetes pod container.
/// </summary>
public sealed record PodExecRequest(
    string Namespace,
    string PodName,
    string? ClusterId = null,
    string? Container = null,
    string? Shell = null,
    int Cols = 80,
    int Rows = 24
);

/// <summary>
/// Represents an active bidirectional PTY exec session into a Kubernetes pod container.
/// </summary>
public interface IPodExecSession : IAsyncDisposable
{
    string ClusterId { get; }
    string Namespace { get; }
    string PodName { get; }
    string? Container { get; }
    string ResolvedShell { get; }

    ValueTask SendInputAsync(string data, CancellationToken ct = default);
    ValueTask ResizeAsync(int cols, int rows, CancellationToken ct = default);
}

/// <summary>
/// Multi-cluster interactive pod exec service supporting real Kubernetes WebSocket PTY streams and Demo Mode simulation.
/// </summary>
public interface IPodExecService
{
    Task<IPodExecSession> AttachAsync(
        PodExecRequest request,
        Func<string, Task> onOutput,
        Func<int, Task> onClosed,
        CancellationToken ct = default);
}

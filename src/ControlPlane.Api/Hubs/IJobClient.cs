namespace ControlPlane.Api.Hubs;

public interface IJobClient
{
    Task ReceiveLogLine(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp);
    Task JobStatusChanged(Guid jobId, string status, string? activeStep);
}

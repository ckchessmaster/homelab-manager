namespace ControlPlane.Api.Features.Agents.Models;

public class AgentCommandEnvelope
{
    public string Type { get; set; } = "EXECUTE_COMMAND";
    public Guid JobId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string[] Args { get; set; } = Array.Empty<string>();
}

public class AgentFrameMessage
{
    public string Type { get; set; } = "FRAME";
    public string NodeId { get; set; } = string.Empty;
    public AgentFrameData Frame { get; set; } = new();
}

public class AgentFrameData
{
    public Guid JobId { get; set; }
    public long SequenceId { get; set; }
    public string StreamType { get; set; } = "stdout";
    public string LogLine { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

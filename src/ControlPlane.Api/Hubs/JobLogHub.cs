using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Api.Hubs;

public class JobLogHub : Hub<IJobClient>
{
    private readonly ILogger<JobLogHub> _logger;

    public JobLogHub(ILogger<JobLogHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinJobGroup(Guid jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId.ToString());
        _logger.LogDebug("Client {ConnectionId} joined log group for job {JobId}", Context.ConnectionId, jobId);
    }

    public async Task LeaveJobGroup(Guid jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId.ToString());
        _logger.LogDebug("Client {ConnectionId} left log group for job {JobId}", Context.ConnectionId, jobId);
    }
}

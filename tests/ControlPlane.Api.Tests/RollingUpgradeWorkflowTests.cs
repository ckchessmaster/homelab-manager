using ControlPlane.Api.Features.Orchestration.Temporal.Workflows;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Xunit;

namespace ControlPlane.Api.Tests;

public class RollingUpgradeWorkflowTests
{
    [Fact]
    public void RollingUpgradeWorkflow_InitialState_IsPending()
    {
        var workflow = new RollingUpgradeWorkflow();
        var state = workflow.GetWorkflowState();

        Assert.Equal("Pending", state.Status);
        Assert.Equal(0, state.TotalHosts);
        Assert.Equal(0, state.CompletedHosts);
        Assert.False(state.IsPaused);
        Assert.False(state.Cancelled);
    }

    [Fact]
    public async Task RollingUpgradeWorkflow_PauseAndResumeSignals_UpdateStateCorrectly()
    {
        var workflow = new RollingUpgradeWorkflow();

        await workflow.PauseAsync();
        var pausedState = workflow.GetWorkflowState();
        Assert.True(pausedState.IsPaused);

        await workflow.ResumeAsync();
        var resumedState = workflow.GetWorkflowState();
        Assert.False(resumedState.IsPaused);
    }

    [Fact]
    public async Task RollingUpgradeWorkflow_CancelSignal_SetsCancelledAndReason()
    {
        var workflow = new RollingUpgradeWorkflow();

        await workflow.CancelAsync("Emergency operator stop");
        var state = workflow.GetWorkflowState();

        Assert.True(state.Cancelled);
        Assert.Equal("Cancelled", state.Status);
        Assert.Equal("Emergency operator stop", state.CancelReason);
    }

    [Fact]
    public void RollingUpgradeWorkflowModels_InputAndResult_BindCorrectly()
    {
        var batchId = Guid.NewGuid();
        var hostId1 = Guid.NewGuid();
        var hostId2 = Guid.NewGuid();

        var targets = new List<RollingHostTarget>
        {
            new(hostId1, "k8s-node-01", "k8s", "ubuntu", "k8s-node-01"),
            new(hostId2, "k8s-node-02", "k8s", "ubuntu", "k8s-node-02"),
        };

        var input = new RollingUpgradeWorkflowInput(
            BatchId: batchId,
            TargetHosts: targets,
            MaxParallelism: 1,
            FailureStrategy: "StopOnFirstFailure",
            RequireApprovalBeforeReboot: true,
            RequireApprovalBetweenHosts: false,
            AlwaysReboot: true,
            ProbeUrls: new List<string> { "http://127.0.0.1:8080/healthz" },
            SnapshotPrefix: "test-snap",
            InitiatedBy: "TestOperator"
        );

        Assert.Equal(batchId, input.BatchId);
        Assert.Equal(2, input.TargetHosts.Count);
        Assert.Equal(1, input.MaxParallelism);
        Assert.Equal("StopOnFirstFailure", input.FailureStrategy);
        Assert.True(input.RequireApprovalBeforeReboot);
        Assert.True(input.AlwaysReboot);
        Assert.Single(input.ProbeUrls!);

        var result = new RollingUpgradeWorkflowResult(
            Success: true,
            Status: "Completed",
            TotalHosts: 2,
            CompletedHosts: 2,
            FailedHosts: 0
        );

        Assert.True(result.Success);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.CompletedHosts);
    }

    [Fact]
    public void RollingHostProgress_PropertiesTrackCorrectly()
    {
        var hostId = Guid.NewGuid();
        var progress = new RollingHostProgress
        {
            HostId = hostId,
            Hostname = "pve-node-01",
            Status = "Running",
            ChildWorkflowId = $"host-upgrade-{hostId}",
            CurrentStep = "Preflight Checks",
            StartedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal("Running", progress.Status);
        Assert.Equal("Preflight Checks", progress.CurrentStep);
        Assert.NotNull(progress.StartedAt);
        Assert.Null(progress.CompletedAt);
        Assert.Null(progress.ErrorMessage);
    }
}

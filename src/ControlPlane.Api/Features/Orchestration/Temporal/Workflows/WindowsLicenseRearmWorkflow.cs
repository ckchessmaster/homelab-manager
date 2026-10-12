using ControlPlane.Api.Features.Orchestration.Temporal.Activities;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public class WindowsLicenseRearmWorkflow : IWindowsLicenseRearmWorkflow
{
    private readonly WindowsLicenseRearmWorkflowState _state = new();

    [WorkflowQuery]
    public WindowsLicenseRearmWorkflowState GetWorkflowState() => _state;

    [WorkflowRun]
    public async Task<WindowsLicenseRearmWorkflowResult> RunAsync(WindowsLicenseRearmWorkflowInput input)
    {
        var defaultOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) };
        var rearmOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(5),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new() { MaximumAttempts = 1 }
        };
        var rebootOptions = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2) };
        var reconnectOptions = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(10),
            HeartbeatTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new() { MaximumAttempts = 1 }
        };

        _state.Status = "Running";
        _state.RebootRequested = input.RebootAfter;

        try
        {
            // Step 1: Preflight check
            _state.ActiveStep = "PreflightCheck";
            await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                a => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", "PreflightCheck", null, input.HostId)),
                defaultOptions);

            var preflightResult = await Workflow.ExecuteActivityAsync<IWindowsLicenseActivities, WindowsLicensePreflightResult>(
                a => a.PreflightCheckAsync(new WindowsLicensePreflightInput(input.JobId, input.HostId, input.Hostname)),
                defaultOptions);

            if (!preflightResult.Success)
            {
                throw new ApplicationFailureException($"Preflight check failed: {preflightResult.Message}");
            }

            _state.RearmsRemaining = preflightResult.RearmsRemaining;
            _state.CompletedSteps.Add("PreflightCheck");

            // Step 2: Execute rearm command
            _state.ActiveStep = "ExecuteRearm";
            await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                a => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", "ExecuteRearm", null, input.HostId)),
                defaultOptions);

            var rearmResult = await Workflow.ExecuteActivityAsync<IWindowsLicenseActivities, WindowsLicenseRearmExecutionResult>(
                a => a.ExecuteRearmAsync(new WindowsLicenseRearmExecutionInput(input.JobId, input.HostId, input.Hostname)),
                rearmOptions);

            if (!rearmResult.Success)
            {
                throw new ApplicationFailureException($"License rearm command failed: {rearmResult.Message}");
            }

            _state.CompletedSteps.Add("ExecuteRearm");

            // Step 3 (Optional): Reboot host if requested
            if (input.RebootAfter)
            {
                _state.ActiveStep = "RebootHost";
                await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                    a => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", "RebootHost", null, input.HostId)),
                    defaultOptions);

                var rebootResult = await Workflow.ExecuteActivityAsync<IAgentActivities, AgentRebootResult>(
                    a => a.InitiateRebootAsync(new AgentRebootInput(input.JobId, input.HostId, input.Hostname, true, 5, "windows")),
                    rebootOptions);

                _state.CompletedSteps.Add("RebootHost");

                // Step 4: Await agent reconnect
                _state.ActiveStep = "WaitForReconnect";
                await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                    a => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "AwaitingReconnect", "WaitForReconnect", null, input.HostId)),
                    defaultOptions);

                var reconnectResult = await Workflow.ExecuteActivityAsync<IAgentActivities, AgentReconnectResult>(
                    a => a.AwaitReconnectionAsync(new AgentReconnectInput(input.JobId, input.HostId, input.Hostname, rebootResult.Skipped, rebootResult.PreRebootKernel, 300)),
                    reconnectOptions);

                if (!reconnectResult.Success)
                {
                    throw new ApplicationFailureException($"Host reconnect verification failed: {reconnectResult.Message}");
                }

                _state.CompletedSteps.Add("WaitForReconnect");
            }

            // Step 5: Refresh license status
            _state.ActiveStep = "RefreshLicenseStatus";
            await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                a => a.UpdateJobStatusAsync(new UpdateJobStatusInput(input.JobId, "Running", "RefreshLicenseStatus", null, input.HostId)),
                defaultOptions);

            var refreshResult = await Workflow.ExecuteActivityAsync<IWindowsLicenseActivities, WindowsLicenseRefreshResult>(
                a => a.RefreshLicenseStatusAsync(new WindowsLicenseRefreshInput(input.JobId, input.HostId, input.Hostname)),
                defaultOptions);

            _state.RearmsRemaining = refreshResult.RearmsRemaining;
            _state.DaysRemaining = refreshResult.DaysRemaining;
            _state.CompletedSteps.Add("RefreshLicenseStatus");

            // Step 6: Complete job
            _state.Status = "Completed";
            _state.ActiveStep = null;
            await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                a => a.RecordJobCompletionAsync(new RecordJobCompletionInput(input.JobId, "Completed", null)),
                defaultOptions);

            return new WindowsLicenseRearmWorkflowResult(
                true,
                "Completed",
                _state.CompletedSteps,
                $"Windows Server license rearm workflow completed successfully for {input.Hostname}."
            );
        }
        catch (Exception ex)
        {
            _state.Status = "Failed";
            _state.FailureReason = ex.Message;
            try
            {
                Workflow.Logger.LogError(ex, "WindowsLicenseRearmWorkflow failed for {Hostname}: {Message}", input.Hostname, ex.Message);
                await Workflow.ExecuteActivityAsync<IUpdateJobActivities>(
                    a => a.RecordJobCompletionAsync(new RecordJobCompletionInput(input.JobId, "Failed", ex.Message)),
                    defaultOptions);
            }
            catch
            {
                // Ignore secondary failure during error reporting
            }

            return new WindowsLicenseRearmWorkflowResult(
                false,
                "Failed",
                _state.CompletedSteps,
                ex.Message
            );
        }
    }
}

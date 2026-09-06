import { apiClient } from './client'

export interface StartTemporalWorkflowRequest {
  hostId: string
  requireApprovalBeforeReboot?: boolean
  alwaysReboot?: boolean
  probeUrls?: string[]
  snapshotName?: string
  k8sNodeName?: string
  initiatedBy?: string
}

export interface StartTemporalWorkflowResponse {
  workflowId: string
  runId?: string
  jobId: string
  hostId: string
}

export interface HostUpgradeWorkflowState {
  status: string
  activeStep?: string | null
  completedSteps: string[]
  awaitingApproval: boolean
  rebootApproved: boolean
  cancelled: boolean
  cancelReason?: string | null
  snapshotIdentifier?: string | null
  k8sNodeName?: string | null
  failureReason?: string | null
}

export interface TemporalWorkflowStatusResponse {
  workflowId: string
  executionStatus: string
  state?: HostUpgradeWorkflowState | null
}

export async function getTemporalWorkflowStatus(
  workflowId: string
): Promise<TemporalWorkflowStatusResponse> {
  return apiClient<TemporalWorkflowStatusResponse>(
    `/api/v1/orchestration/temporal/workflows/${encodeURIComponent(workflowId)}/status`
  )
}

export async function sendTemporalWorkflowSignal(
  workflowId: string,
  signalName: string,
  reason?: string
): Promise<{ success: boolean; signal: string; reason?: string }> {
  return apiClient(
    `/api/v1/orchestration/temporal/workflows/${encodeURIComponent(workflowId)}/signals/${encodeURIComponent(signalName)}`,
    {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }
  )
}

export async function startTemporalWorkflow(
  request: StartTemporalWorkflowRequest
): Promise<StartTemporalWorkflowResponse> {
  return apiClient<StartTemporalWorkflowResponse>(
    '/api/v1/orchestration/temporal/workflows/start',
    {
      method: 'POST',
      body: JSON.stringify(request),
    }
  )
}

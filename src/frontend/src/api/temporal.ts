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
  skippedSteps?: string[]
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

// Batch Fleet Rolling Upgrade
export interface StartRollingUpgradeRequest {
  hostIds: string[]
  maxParallelism?: number
  failureStrategy?: 'StopOnFirstFailure' | 'ContinueRemaining'
  requireApprovalBeforeReboot?: boolean
  requireApprovalBetweenHosts?: boolean
  alwaysReboot?: boolean
  probeUrls?: string[]
  snapshotPrefix?: string
  initiatedBy?: string
}

export interface StartRollingUpgradeResponse {
  batchId: string
  workflowId: string
  totalHosts: number
  targetHostIds: string[]
}

export interface RollingHostProgress {
  hostId: string
  hostname: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped'
  childWorkflowId?: string | null
  currentStep?: string | null
  errorMessage?: string | null
  startedAt?: string | null
  completedAt?: string | null
}

export interface RollingUpgradeWorkflowState {
  batchId: string
  status: 'Pending' | 'Running' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled' | 'PartiallyFailed'
  totalHosts: number
  completedHosts: number
  failedHosts: number
  activeHostId?: string | null
  activeHostname?: string | null
  isPaused: boolean
  cancelled: boolean
  cancelReason?: string | null
  failureReason?: string | null
  hostProgresses: Record<string, RollingHostProgress>
}

export interface RollingUpgradeStatusResponse {
  workflowId: string
  executionStatus: string
  state?: RollingUpgradeWorkflowState | null
}

export async function startRollingUpgrade(
  request: StartRollingUpgradeRequest
): Promise<StartRollingUpgradeResponse> {
  return apiClient<StartRollingUpgradeResponse>(
    '/api/v1/orchestration/temporal/batch/rolling-upgrade',
    {
      method: 'POST',
      body: JSON.stringify(request),
    }
  )
}

export async function getRollingUpgradeStatus(
  batchId: string
): Promise<RollingUpgradeStatusResponse> {
  return apiClient<RollingUpgradeStatusResponse>(
    `/api/v1/orchestration/temporal/batch/${encodeURIComponent(batchId)}/status`
  )
}

export async function sendRollingUpgradeSignal(
  batchId: string,
  signalName: string,
  reason?: string
): Promise<{ success: boolean; signal: string; workflowId: string; reason?: string }> {
  return apiClient(
    `/api/v1/orchestration/temporal/batch/${encodeURIComponent(batchId)}/signals/${encodeURIComponent(signalName)}`,
    {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }
  )
}

export interface RollingBatchSummary {
  batchId: string
  workflowId: string
  status: string
  totalHosts: number
  completedHosts: number
  failedHosts: number
  activeHostname?: string | null
  isPaused: boolean
  hostIds: string[]
  hostnames: string[]
  initiatedBy: string
  startedAt: string
}

export async function listRollingBatches(): Promise<RollingBatchSummary[]> {
  return apiClient<RollingBatchSummary[]>('/api/v1/orchestration/temporal/batch')
}

export async function getActiveRollingBatch(): Promise<RollingBatchSummary | null> {
  return apiClient<RollingBatchSummary | null>('/api/v1/orchestration/temporal/batch/active')
}

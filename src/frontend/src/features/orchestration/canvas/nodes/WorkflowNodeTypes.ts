import type { Node } from '@xyflow/react'

export type ActivityStatus = 'pending' | 'running' | 'completed' | 'failed' | 'compensated' | 'skipped'

export interface ActivityNodeData extends Record<string, unknown> {
  label: string
  category: 'preflight' | 'proxmox' | 'kubernetes' | 'agent' | 'health' | 'complete'
  stepNumber: number
  status: ActivityStatus
  duration?: string
  description?: string
  errorMessage?: string
  isCompensation?: boolean
  startedAt?: string
  completedAt?: string
}

export interface ApprovalGateNodeData extends Record<string, unknown> {
  label: string
  status: 'waiting' | 'approved' | 'rejected' | 'timeout'
  workflowId: string
  onApprove?: () => void
  onReject?: () => void
  isSubmitting?: boolean
  timeoutCountdown?: string
}

export type WorkflowActivityNodeType = Node<ActivityNodeData, 'activity'>
export type ApprovalGateNodeType = Node<ApprovalGateNodeData, 'approvalGate'>

export type WorkflowCanvasNode = WorkflowActivityNodeType | ApprovalGateNodeType

import type { Edge, Node } from '@xyflow/react'
import type { ActivityStatus, ActivityNodeData, ApprovalGateNodeData } from '../nodes/WorkflowNodeTypes'
import type { AnimatedWorkflowEdgeData } from '../edges/WorkflowEdgeTypes'

export interface WorkflowStateLike {
  status: string
  activeStep?: string | null
  completedSteps?: string[]
  awaitingApproval?: boolean
  rebootApproved?: boolean
  cancelled?: boolean
  failureReason?: string | null
  snapshotIdentifier?: string | null
  k8sNodeName?: string | null
}

export interface StepDefinition {
  id: string
  label: string
  category: 'preflight' | 'proxmox' | 'kubernetes' | 'agent' | 'health' | 'complete'
  description?: string
  isApprovalGate?: boolean
  hasCompensation?: boolean
  compensationLabel?: string
  compensationCategory?: 'proxmox' | 'kubernetes'
}

// Canonical default steps for HostUpgradeWorkflow
export const DEFAULT_HOST_UPGRADE_STEPS: StepDefinition[] = [
  {
    id: 'hb',
    label: 'Preflight: Heartbeat Freshness',
    category: 'preflight',
    description: 'Verify agent heartbeat is recent (< 15s) and responsive.',
  },
  {
    id: 'disk',
    label: 'Preflight: Disk Headroom',
    category: 'preflight',
    description: 'Verify root filesystem has at least 20% free space.',
  },
  {
    id: 'lock',
    label: 'Preflight: Package Lock Check',
    category: 'preflight',
    description: 'Inspect apt/dnf locks to prevent package manager collisions.',
  },
  {
    id: 'snapshot',
    label: 'Proxmox: Create Snapshot',
    category: 'proxmox',
    description: 'Atomic ZFS/QEMU snapshot before initiating upgrade.',
    hasCompensation: true,
    compensationLabel: 'Saga: Rollback Snapshot',
    compensationCategory: 'proxmox',
  },
  {
    id: 'cordon_drain',
    label: 'Kubernetes: Cordon & Drain',
    category: 'kubernetes',
    description: 'Cordon node and evict non-daemonset pods via Eviction API.',
    hasCompensation: true,
    compensationLabel: 'Saga: Uncordon Node',
    compensationCategory: 'kubernetes',
  },
  {
    id: 'upgrade',
    label: 'Agent: Package Upgrade',
    category: 'agent',
    description: 'Execute non-interactive distribution upgrade with activity heartbeats.',
  },
  {
    id: 'approval_gate',
    label: 'Reboot Approval Gate',
    category: 'agent',
    description: 'Operator action required before proceeding to host restart.',
    isApprovalGate: true,
  },
  {
    id: 'reboot',
    label: 'Agent: Deterministic Reboot',
    category: 'agent',
    description: 'Synchronous shutdown notification followed by systemctl reboot.',
  },
  {
    id: 'reconnect',
    label: 'Agent: Reconnection Wait',
    category: 'agent',
    description: 'Poll agent reconnection over WebSocket until online.',
  },
  {
    id: 'health_probes',
    label: 'Health Probes: HTTP/TCP Probes',
    category: 'health',
    description: 'Synthetic endpoint polling to verify services are responsive.',
  },
  {
    id: 'uncordon',
    label: 'Kubernetes: Uncordon Node',
    category: 'kubernetes',
    description: 'Mark node as schedulable and verify cluster health.',
  },
]

export function computeStepStatus(
  step: StepDefinition,
  state: WorkflowStateLike
): ActivityStatus {
  const completed = state.completedSteps || []
  const active = state.activeStep || ''
  const isWorkflowFailed = state.status === 'Failed' || state.status === 'RolledBack'
  const isWorkflowCompleted = state.status === 'Completed'

  // Match step by label or id
  const isMatchedCompleted = completed.some((c) =>
    c.toLowerCase().includes(step.label.toLowerCase()) ||
    step.label.toLowerCase().includes(c.toLowerCase()) ||
    (step.id === 'reboot' && c.toLowerCase().includes('reboot')) ||
    (step.id === 'reconnect' && c.toLowerCase().includes('reconnect'))
  )

  if (isMatchedCompleted) return 'completed'

  const isMatchedActive =
    active.toLowerCase().includes(step.label.toLowerCase()) ||
    step.label.toLowerCase().includes(active.toLowerCase()) ||
    (step.id === 'approval_gate' && state.awaitingApproval)

  if (isMatchedActive) {
    if (isWorkflowFailed) return 'failed'
    return 'running'
  }

  if (isWorkflowCompleted) return 'completed'

  return 'pending'
}

export interface DagLayoutResult {
  nodes: Node[]
  edges: Edge[]
}

export function buildDagFromState(
  state: WorkflowStateLike,
  options: {
    workflowId: string
    onApprove?: () => void
    onReject?: () => void
    isSubmittingApproval?: boolean
    requireApproval?: boolean
    isProxmoxHost?: boolean
    isK8sHost?: boolean
  }
): DagLayoutResult {
  const nodes: Node[] = []
  const edges: Edge[] = []

  // Filter steps based on host capabilities
  const activeSteps = DEFAULT_HOST_UPGRADE_STEPS.filter((step) => {
    if (step.category === 'proxmox' && options.isProxmoxHost === false) return false
    if (step.category === 'kubernetes' && options.isK8sHost === false) return false
    if (step.isApprovalGate && options.requireApproval === false) return false
    return true
  })

  const NODE_SPACING_X = 330
  const BASE_Y = 160
  const COMPENSATION_Y = 360

  let lastNodeId: string | null = null
  let stepIndex = 1

  activeSteps.forEach((step, idx) => {
    const x = idx * NODE_SPACING_X + 60
    const y = BASE_Y

    if (step.isApprovalGate) {
      let gateStatus: 'waiting' | 'approved' | 'rejected' | 'timeout' = 'waiting'
      if (state.rebootApproved) {
        gateStatus = 'approved'
      } else if (state.cancelled) {
        gateStatus = 'rejected'
      } else if (state.awaitingApproval) {
        gateStatus = 'waiting'
      } else {
        const approvalPassed = (state.completedSteps || []).some((c) =>
          c.toLowerCase().includes('reboot')
        )
        gateStatus = approvalPassed ? 'approved' : 'waiting'
      }

      const gateNode: Node<ApprovalGateNodeData, 'approvalGate'> = {
        id: step.id,
        type: 'approvalGate',
        position: { x, y: y - 20 },
        data: {
          label: step.label,
          status: gateStatus,
          workflowId: options.workflowId,
          onApprove: options.onApprove,
          onReject: options.onReject,
          isSubmitting: options.isSubmittingApproval,
        },
      }
      nodes.push(gateNode)
    } else {
      const status = computeStepStatus(step, state)
      const isFailed = status === 'failed'

      const activityNode: Node<ActivityNodeData, 'activity'> = {
        id: step.id,
        type: 'activity',
        position: { x, y },
        data: {
          label: step.label,
          category: step.category,
          stepNumber: stepIndex++,
          status,
          description: step.description,
          errorMessage: isFailed ? state.failureReason || 'Activity execution failed' : undefined,
        },
      }
      nodes.push(activityNode)

      // If step has Saga compensation, add compensation node branched below
      if (step.hasCompensation && step.compensationLabel) {
        const compId = `${step.id}_comp`
        const isCompActive = state.status === 'RolledBack' || (state.status === 'Failed' && status === 'completed')
        const compStatus: ActivityStatus = isCompActive ? 'compensated' : 'pending'

        const compNode: Node<ActivityNodeData, 'activity'> = {
          id: compId,
          type: 'activity',
          position: { x, y: COMPENSATION_Y },
          data: {
            label: step.compensationLabel,
            category: step.compensationCategory || 'proxmox',
            stepNumber: 0,
            status: compStatus,
            description: `Automated Saga compensation if subsequent steps fail.`,
            isCompensation: true,
          },
        }
        nodes.push(compNode)

        // Edge down to compensation node
        edges.push({
          id: `edge_${step.id}_to_comp`,
          type: 'workflowEdge',
          source: step.id,
          target: compId,
          data: {
            status: isCompActive ? 'compensated' : 'pending',
            isCompensation: true,
            label: 'Rollback Branch',
          } as AnimatedWorkflowEdgeData,
        })
      }
    }

    // Connect sequential edge from previous step
    if (lastNodeId) {
      const prevStep = activeSteps[idx - 1]
      const prevStatus = prevStep.isApprovalGate
        ? state.rebootApproved ? 'completed' : 'pending'
        : computeStepStatus(prevStep, state)

      const currStatus = step.isApprovalGate
        ? state.awaitingApproval ? 'running' : state.rebootApproved ? 'completed' : 'pending'
        : computeStepStatus(step, state)

      let edgeStatus: 'pending' | 'running' | 'completed' | 'failed' = 'pending'
      if (currStatus === 'running') {
        edgeStatus = 'running'
      } else if (prevStatus === 'completed' && currStatus === 'completed') {
        edgeStatus = 'completed'
      } else if (currStatus === 'failed') {
        edgeStatus = 'failed'
      }

      edges.push({
        id: `edge_${lastNodeId}_to_${step.id}`,
        type: 'workflowEdge',
        source: lastNodeId,
        target: step.id,
        data: {
          status: edgeStatus,
        } as AnimatedWorkflowEdgeData,
      })
    }

    lastNodeId = step.id
  })

  return { nodes, edges }
}

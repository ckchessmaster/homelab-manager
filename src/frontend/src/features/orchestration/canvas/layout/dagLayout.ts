import type { Edge, Node } from '@xyflow/react'
import type { ActivityStatus, ActivityNodeData, ApprovalGateNodeData } from '../nodes/WorkflowNodeTypes'
import type { AnimatedWorkflowEdgeData } from '../edges/WorkflowEdgeTypes'

export interface WorkflowStateLike {
  status: string
  activeStep?: string | null
  completedSteps?: string[]
  skippedSteps?: string[]
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

// Canonical default steps for safe reboot workflows
export const DEFAULT_SAFE_REBOOT_STEPS: StepDefinition[] = [
  {
    id: 'hb',
    label: 'Preflight: Heartbeat Freshness',
    category: 'preflight',
    description: 'Verify active WebSocket connection before rebooting.',
  },
  {
    id: 'cordon_drain',
    label: 'Kubernetes: Cordon & Drain',
    category: 'kubernetes',
    description: 'Cordon node and evict non-daemonset pods via Eviction API prior to restart.',
    hasCompensation: true,
    compensationLabel: 'Saga: Uncordon Node',
    compensationCategory: 'kubernetes',
  },
  {
    id: 'reboot',
    label: 'Agent: Deterministic Reboot',
    category: 'agent',
    description: 'Pre-reboot filesystem sync and controlled reboot command emission.',
  },
  {
    id: 'reconnect',
    label: 'Agent: Reconnection Wait',
    category: 'agent',
    description: 'Monitors WebSocket reconnection window following host reboot.',
  },
  {
    id: 'health_probes',
    label: 'Health Probes: HTTP/TCP Probes',
    category: 'health',
    description: 'Runs automated post-boot sanity checks on network and key services.',
  },
  {
    id: 'uncordon',
    label: 'Kubernetes: Uncordon Node',
    category: 'kubernetes',
    description: 'Marks node as schedulable to resume workload processing.',
  },
]

export function matchesStep(step: StepDefinition, text?: string | null): boolean {
  if (!text || !text.trim()) return false
  const t = text.trim().toLowerCase()
  const label = step.label.toLowerCase()

  if (t === label || t === step.id) return true
  if (t.includes(label)) return true

  switch (step.id) {
    case 'hb':
      return t.includes('heartbeat')
    case 'disk':
      return t.includes('disk') || t.includes('headroom')
    case 'lock':
      return t.includes('lock')
    case 'snapshot':
      return t.includes('snapshot') && !t.includes('rollback')
    case 'cordon_drain':
      return (t.includes('cordon') || t.includes('drain') || t.includes('evict')) && !t.includes('uncordon')
    case 'upgrade':
      return (
        t.includes('package upgrade') ||
        t.includes('distribution upgrade') ||
        t.includes('upgrade execution') ||
        (t.includes('upgrade') && !t.includes('rolling'))
      )
    case 'approval_gate':
      return t.includes('approval')
    case 'reboot':
      return t.includes('reboot') && !t.includes('approval')
    case 'reconnect':
      return t.includes('reconnection') || t.includes('reconnect')
    case 'health_probes':
      return t.includes('probe') || (t.includes('health') && !t.includes('heartbeat'))
    case 'uncordon':
      return t.includes('uncordon')
    default:
      return label.includes(t) && t.length >= 4
  }
}

export function isStepCompleted(step: StepDefinition, completedSteps?: string[] | null): boolean {
  if (!completedSteps || completedSteps.length === 0) return false
  return completedSteps.some((c) => matchesStep(step, c))
}

export function isStepSkipped(step: StepDefinition, skippedSteps?: string[] | null): boolean {
  if (!skippedSteps || skippedSteps.length === 0) return false
  return skippedSteps.some((c) => matchesStep(step, c))
}

export function resolveStepStatuses(
  steps: StepDefinition[],
  state: WorkflowStateLike
): Map<string, ActivityStatus> {
  const result = new Map<string, ActivityStatus>()
  const isWorkflowCompleted = state.status === 'Completed'
  const isWorkflowFailed = state.status === 'Failed' || state.status === 'RolledBack' || state.status === 'Cancelled'

  if (isWorkflowCompleted) {
    for (const step of steps) {
      if (isStepSkipped(step, state.skippedSteps)) {
        result.set(step.id, 'skipped')
      } else if (state.completedSteps && state.completedSteps.length > 0) {
        result.set(step.id, isStepCompleted(step, state.completedSteps) ? 'completed' : 'skipped')
      } else {
        result.set(step.id, 'completed')
      }
    }
    return result
  }

  // Find index of actively failing or running step
  let activeIndex = -1
  if (state.activeStep) {
    activeIndex = steps.findIndex((s) => matchesStep(s, state.activeStep))
  }
  if (activeIndex === -1 && state.awaitingApproval) {
    activeIndex = steps.findIndex((s) => s.isApprovalGate)
  }

  // If workflow failed but no activeStep matched, find first non-completed step
  if (isWorkflowFailed && activeIndex === -1) {
    activeIndex = steps.findIndex((s) => !isStepCompleted(s, state.completedSteps) && !isStepSkipped(s, state.skippedSteps))
    if (activeIndex === -1) {
      activeIndex = steps.length - 1
    }
  }

  for (let i = 0; i < steps.length; i++) {
    const step = steps[i]
    if (isStepSkipped(step, state.skippedSteps)) {
      result.set(step.id, 'skipped')
      continue
    }

    if (activeIndex >= 0) {
      if (i < activeIndex) {
        result.set(step.id, 'completed')
      } else if (i === activeIndex) {
        result.set(step.id, isWorkflowFailed ? 'failed' : 'running')
      } else {
        result.set(step.id, 'pending')
      }
    } else {
      const completed = isStepCompleted(step, state.completedSteps)
      result.set(step.id, completed ? 'completed' : 'pending')
    }
  }

  return result
}

export function computeStepStatus(
  step: StepDefinition,
  state: WorkflowStateLike
): ActivityStatus {
  return resolveStepStatuses([step], state).get(step.id) || 'pending'
}

export interface DagLayoutResult {
  nodes: Node[]
  edges: Edge[]
}

export function buildDagFromState(
  state: WorkflowStateLike,
  options: {
    workflowId: string
    pipelineId?: string | null
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

  const isRebootPipeline = Boolean(
    options.pipelineId &&
    (options.pipelineId.includes('reboot') || options.pipelineId.includes('safe-reboot'))
  )

  const baseStepList = isRebootPipeline ? DEFAULT_SAFE_REBOOT_STEPS : DEFAULT_HOST_UPGRADE_STEPS

  // Detect capabilities directly from workflow execution state or options
  const hasK8sExecution = Boolean(
    state.k8sNodeName ||
    state.activeStep?.toLowerCase().includes('kubernetes') ||
    state.activeStep?.toLowerCase().includes('evict') ||
    state.activeStep?.toLowerCase().includes('cordon') ||
    state.activeStep?.toLowerCase().includes('drain') ||
    state.completedSteps?.some((s) => s.toLowerCase().includes('kubernetes') || s.toLowerCase().includes('cordon') || s.toLowerCase().includes('drain') || s.toLowerCase().includes('evict')) ||
    state.skippedSteps?.some((s) => s.toLowerCase().includes('kubernetes')) ||
    options.pipelineId?.toLowerCase().includes('k8s')
  )

  const hasProxmoxExecution = Boolean(
    state.snapshotIdentifier ||
    state.activeStep?.toLowerCase().includes('snapshot') ||
    state.completedSteps?.some((s) => s.toLowerCase().includes('snapshot')) ||
    options.pipelineId?.toLowerCase().includes('proxmox')
  )

  const hasApprovalExecution = Boolean(
    state.awaitingApproval ||
    state.rebootApproved ||
    state.activeStep?.toLowerCase().includes('approval') ||
    state.completedSteps?.some((s) => s.toLowerCase().includes('approval'))
  )

  const shouldIncludeK8s = hasK8sExecution || options.isK8sHost === true
  const shouldIncludeProxmox = hasProxmoxExecution || options.isProxmoxHost === true
  const shouldIncludeApproval = hasApprovalExecution || options.requireApproval === true

  // Filter steps based on host capabilities & actual execution
  const activeSteps = baseStepList.filter((step) => {
    if (step.category === 'proxmox' && !shouldIncludeProxmox) return false
    if (step.category === 'kubernetes' && !shouldIncludeK8s) return false
    if (step.isApprovalGate && !shouldIncludeApproval) return false
    return true
  })

  const NODE_SPACING_X = 330
  const BASE_Y = 160
  const COMPENSATION_Y = 360

  let lastNodeId: string | null = null
  let stepIndex = 1
  const stepStatusMap = resolveStepStatuses(activeSteps, state)

  activeSteps.forEach((step, idx) => {
    const x = idx * NODE_SPACING_X + 60
    const y = BASE_Y

    if (step.isApprovalGate) {
      let gateStatus: 'waiting' | 'approved' | 'rejected' | 'timeout' = 'waiting'
      const statusFromMap = stepStatusMap.get(step.id)
      if (state.rebootApproved) {
        gateStatus = 'approved'
      } else if (state.cancelled) {
        gateStatus = 'rejected'
      } else if (statusFromMap === 'failed') {
        gateStatus = state.cancelled ? 'rejected' : 'timeout'
      } else if (state.awaitingApproval) {
        gateStatus = 'waiting'
      } else {
        const approvalPassed = isStepCompleted(step, state.completedSteps) || (state.completedSteps || []).some((c) =>
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
      const status = stepStatusMap.get(step.id) || 'pending'
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
        : stepStatusMap.get(prevStep.id) || 'pending'

      const currStatus = step.isApprovalGate
        ? state.awaitingApproval ? 'running' : state.rebootApproved ? 'completed' : 'pending'
        : stepStatusMap.get(step.id) || 'pending'

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

import type { Edge } from '@xyflow/react'

export type EdgeFlowStatus = 'pending' | 'running' | 'completed' | 'failed' | 'compensated'

export interface AnimatedWorkflowEdgeData extends Record<string, unknown> {
  status: EdgeFlowStatus
  isCompensation?: boolean
  label?: string
}

export type AnimatedWorkflowEdgeType = Edge<AnimatedWorkflowEdgeData, 'workflowEdge'>

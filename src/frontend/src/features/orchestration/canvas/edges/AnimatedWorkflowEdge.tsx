import { memo } from 'react'
import { BaseEdge, getSmoothStepPath } from '@xyflow/react'
import type { EdgeProps } from '@xyflow/react'
import type { AnimatedWorkflowEdgeData } from './WorkflowEdgeTypes'

export const AnimatedWorkflowEdge: React.FC<EdgeProps> = memo(({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
  data,
}) => {
  const edgeData = data as unknown as AnimatedWorkflowEdgeData | undefined
  const status = edgeData?.status || 'pending'
  const isCompensation = edgeData?.isCompensation || false

  const [edgePath] = getSmoothStepPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
    borderRadius: 16,
  })

  let strokeColor = '#3f3f46' // zinc-700
  let strokeWidth = 2
  let strokeDasharray = undefined
  let className = ''

  if (isCompensation) {
    strokeColor = '#f97316' // orange-500
    strokeDasharray = '5 5'
  } else if (status === 'completed') {
    strokeColor = '#10b981' // emerald-500
    strokeWidth = 2.5
  } else if (status === 'running') {
    strokeColor = '#38bdf8' // sky-400
    strokeWidth = 2.5
    strokeDasharray = '6 6'
    className = 'animate-flow'
  } else if (status === 'failed') {
    strokeColor = '#f43f5e' // rose-500
    strokeWidth = 2
  }

  return (
    <>
      <BaseEdge
        id={id}
        path={edgePath}
        markerEnd={markerEnd}
        style={{
          ...style,
          stroke: strokeColor,
          strokeWidth,
          strokeDasharray,
          transition: 'stroke 0.3s ease, stroke-width 0.3s ease',
        }}
        className={className}
      />
      {status === 'running' && (
        <circle r="4" fill="#38bdf8" filter="drop-shadow(0 0 4px #0ea5e9)">
          <animateMotion dur="1.5s" repeatCount="indefinite" path={edgePath} />
        </circle>
      )}
    </>
  )
})

AnimatedWorkflowEdge.displayName = 'AnimatedWorkflowEdge'

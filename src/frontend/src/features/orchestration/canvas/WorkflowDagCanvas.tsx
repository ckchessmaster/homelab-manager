import { useMemo, useEffect } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  BackgroundVariant,
  useReactFlow,
  ReactFlowProvider,
  type Node,
} from '@xyflow/react'
import { WorkflowActivityNode } from './nodes/WorkflowActivityNode'
import { ApprovalGateNode } from './nodes/ApprovalGateNode'
import { AnimatedWorkflowEdge } from './edges/AnimatedWorkflowEdge'
import {
  buildDagFromState,
  type WorkflowStateLike,
} from './layout/dagLayout'
import type { ActivityNodeData } from './nodes/WorkflowNodeTypes'

const nodeTypes = {
  activity: WorkflowActivityNode,
  approvalGate: ApprovalGateNode,
}

const edgeTypes = {
  workflowEdge: AnimatedWorkflowEdge,
}

export interface WorkflowDagCanvasProps {
  workflowId: string
  state: WorkflowStateLike
  onApprove?: () => void
  onReject?: () => void
  isSubmittingApproval?: boolean
  isProxmoxHost?: boolean
  isK8sHost?: boolean
  requireApproval?: boolean
  className?: string
  height?: string | number
}

function InnerWorkflowCanvas({
  workflowId,
  state,
  onApprove,
  onReject,
  isSubmittingApproval,
  isProxmoxHost,
  isK8sHost,
  requireApproval,
  className = '',
  height = '500px',
}: WorkflowDagCanvasProps) {
  const { fitView } = useReactFlow()

  const { initialNodes, initialEdges } = useMemo(() => {
    const { nodes, edges } = buildDagFromState(state, {
      workflowId,
      onApprove,
      onReject,
      isSubmittingApproval,
      requireApproval,
      isProxmoxHost,
      isK8sHost,
    })
    return { initialNodes: nodes, initialEdges: edges }
  }, [
    state,
    workflowId,
    onApprove,
    onReject,
    isSubmittingApproval,
    requireApproval,
    isProxmoxHost,
    isK8sHost,
  ])

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges)

  useEffect(() => {
    setNodes(initialNodes)
    setEdges(initialEdges)
    const timer = setTimeout(() => {
      fitView({ padding: 0.2, duration: 400 })
    }, 100)
    return () => clearTimeout(timer)
  }, [initialNodes, initialEdges, fitView, setNodes, setEdges])

  const minimapNodeColor = (node: Node) => {
    if (node.type === 'approvalGate') return '#f59e0b'
    const data = node.data as unknown as ActivityNodeData
    switch (data?.status) {
      case 'completed':
        return '#10b981'
      case 'running':
        return '#38bdf8'
      case 'failed':
        return '#f43f5e'
      case 'compensated':
        return '#f97316'
      default:
        return '#3f3f46'
    }
  }

  return (
    <div
      className={`relative w-full rounded-xl overflow-hidden border border-zinc-800 bg-zinc-950 ${className}`}
      style={{ height }}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        minZoom={0.2}
        maxZoom={1.5}
        defaultEdgeOptions={{ type: 'workflowEdge' }}
        proOptions={{ hideAttribution: true }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={20}
          size={1.5}
          color="#27272a"
        />
        <Controls
          className="!bg-zinc-900 !border-zinc-800 !shadow-xl !rounded-lg overflow-hidden [&>button]:!bg-zinc-900 [&>button]:!border-zinc-800 [&>button]:!text-zinc-300 hover:[&>button]:!bg-zinc-800 hover:[&>button]:!text-zinc-100"
        />
        <MiniMap
          nodeColor={minimapNodeColor}
          nodeStrokeWidth={2}
          maskColor="rgba(9, 9, 11, 0.75)"
          className="!bg-zinc-900/90 !border-zinc-800 !rounded-lg overflow-hidden"
          zoomable
          pannable
        />
      </ReactFlow>
    </div>
  )
}

export function WorkflowDagCanvas(props: WorkflowDagCanvasProps) {
  return (
    <ReactFlowProvider>
      <InnerWorkflowCanvas {...props} />
    </ReactFlowProvider>
  )
}

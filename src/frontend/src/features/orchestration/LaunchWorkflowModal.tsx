import type { Host } from '../../api/hosts'
import { ParameterizedWorkflowLauncher } from './launcher/ParameterizedWorkflowLauncher'

export interface LaunchWorkflowModalProps {
  isOpen: boolean
  onClose: () => void
  host?: Host | null
  availableHosts?: Host[]
  onWorkflowLaunched: (jobId: string, host: Host, workflowId?: string) => void
}

export function LaunchWorkflowModal({
  isOpen,
  onClose,
  host,
  availableHosts,
  onWorkflowLaunched,
}: LaunchWorkflowModalProps) {
  return (
    <ParameterizedWorkflowLauncher
      isOpen={isOpen}
      onClose={onClose}
      initialHost={host}
      availableHosts={availableHosts}
      onWorkflowLaunched={onWorkflowLaunched}
    />
  )
}

import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { MobileDagTimeline } from './MobileDagTimeline'
import type { WorkflowStateLike } from './layout/dagLayout'

describe('MobileDagTimeline', () => {
  it('renders sequential steps with status indicators', () => {
    const state: WorkflowStateLike = {
      status: 'Running',
      activeStep: 'Package Upgrade Execution',
      completedSteps: ['preflight'],
      skippedSteps: [],
    }

    render(
      <MobileDagTimeline
        state={state}
        pipelineId="standard-os-upgrade"
      />
    )

    expect(screen.getByText('Execution Timeline')).toBeInTheDocument()
    expect(screen.getByText('Preflight Verification')).toBeInTheDocument()
    expect(screen.getByText('Package Upgrade Execution')).toBeInTheDocument()
    expect(screen.getByText('Post-Flight Health Probes')).toBeInTheDocument()
  })

  it('renders approval buttons when awaiting approval', () => {
    const onApprove = vi.fn()
    const onReject = vi.fn()

    const state: WorkflowStateLike = {
      status: 'Running',
      activeStep: 'Reboot & Kernel Verification',
      completedSteps: ['preflight', 'packages'],
      skippedSteps: [],
      awaitingApproval: true,
    }

    render(
      <MobileDagTimeline
        state={state}
        onApprove={onApprove}
        onReject={onReject}
      />
    )

    const approveBtn = screen.getByRole('button', { name: /Approve Reboot/i })
    const rejectBtn = screen.getByRole('button', { name: /Reject & Rollback/i })

    expect(approveBtn).toBeInTheDocument()
    expect(rejectBtn).toBeInTheDocument()

    fireEvent.click(approveBtn)
    expect(onApprove).toHaveBeenCalledTimes(1)
  })
})

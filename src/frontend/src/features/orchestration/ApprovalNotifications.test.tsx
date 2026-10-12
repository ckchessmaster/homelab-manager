import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ApprovalToastNotification } from './ApprovalToastNotification'
import { QuickApprovalModal } from './QuickApprovalModal'
import { isJobAwaitingApproval } from './usePendingApprovals'
import type { JobSummary } from '../../api/jobs'

// Mock useHosts
vi.mock('../hosts/useHosts', () => ({
  useHosts: () => ({
    data: [
      {
        id: 'host-1',
        hostname: 'node-alpha',
        ipAddress: '192.168.1.50',
      },
      {
        id: 'host-2',
        hostname: 'node-beta',
        ipAddress: '192.168.1.51',
      },
    ],
  }),
}))

// Mock usePendingApprovals
const mockApproveJob = vi.fn()
const mockRejectJob = vi.fn()
const mockApproveAll = vi.fn()

let mockPendingState = {
  pendingApprovals: [] as JobSummary[],
  pendingCount: 0,
  actionInProgressId: null as string | null,
  approveJob: mockApproveJob,
  rejectJob: mockRejectJob,
  approveAll: mockApproveAll,
}

vi.mock('./usePendingApprovals', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./usePendingApprovals')>()
  return {
    ...actual,
    usePendingApprovals: () => mockPendingState,
  }
})

describe('isJobAwaitingApproval', () => {
  it('returns true when job status is AwaitingApproval', () => {
    const job: JobSummary = {
      id: 'job-1',
      targetHostId: 'host-1',
      status: 'AwaitingApproval',
      initiatedBy: 'Operator',
    }
    expect(isJobAwaitingApproval(job)).toBe(true)
  })

  it('returns true when activeStep mentions approval', () => {
    const job: JobSummary = {
      id: 'job-2',
      targetHostId: 'host-1',
      status: 'Running',
      activeStep: 'Awaiting Operator Reboot Approval',
      initiatedBy: 'Operator',
    }
    expect(isJobAwaitingApproval(job)).toBe(true)
  })

  it('returns false for ordinary running or completed jobs', () => {
    const job: JobSummary = {
      id: 'job-3',
      targetHostId: 'host-1',
      status: 'Running',
      activeStep: 'Applying package upgrades',
      initiatedBy: 'Operator',
    }
    expect(isJobAwaitingApproval(job)).toBe(false)
  })
})

describe('ApprovalToastNotification', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('does not render when there are no pending approvals', () => {
    const onOpenApprovals = vi.fn()
    const { container } = render(
      <ApprovalToastNotification
        pendingApprovals={[]}
        onOpenApprovals={onOpenApprovals}
      />
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('renders notification toast with hostname and triggers onOpenApprovals when clicked', () => {
    const onOpenApprovals = vi.fn()
    const sampleJobs: JobSummary[] = [
      {
        id: 'job-1',
        targetHostId: 'host-1',
        status: 'AwaitingApproval',
        initiatedBy: 'Operator',
      },
    ]

    render(
      <ApprovalToastNotification
        pendingApprovals={sampleJobs}
        onOpenApprovals={onOpenApprovals}
      />
    )

    expect(screen.getByText('Action Required: Reboot Approval')).toBeInTheDocument()
    expect(screen.getByText('node-alpha')).toBeInTheDocument()

    const reviewBtn = screen.getByRole('button', { name: /Review & Approve/i })
    fireEvent.click(reviewBtn)
    expect(onOpenApprovals).toHaveBeenCalledTimes(1)
  })

  it('dismisses toast when close button is clicked', () => {
    const onOpenApprovals = vi.fn()
    const sampleJobs: JobSummary[] = [
      {
        id: 'job-1',
        targetHostId: 'host-1',
        status: 'AwaitingApproval',
        initiatedBy: 'Operator',
      },
    ]

    render(
      <ApprovalToastNotification
        pendingApprovals={sampleJobs}
        onOpenApprovals={onOpenApprovals}
      />
    )

    const dismissBtn = screen.getByRole('button', { name: /Dismiss approval notification/i })
    fireEvent.click(dismissBtn)

    expect(screen.queryByText('Action Required: Reboot Approval')).not.toBeInTheDocument()
  })
})

describe('QuickApprovalModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('does not render when isOpen is false', () => {
    const { container } = render(
      <QuickApprovalModal
        isOpen={false}
        onClose={vi.fn()}
      />
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('renders pending approval with host details and allows approval', () => {
    const testJob: JobSummary = {
      id: 'job-1',
      targetHostId: 'host-1',
      status: 'AwaitingApproval',
      activeStep: 'Awaiting Operator Reboot Approval',
      pipelineId: 'standard-os-upgrade',
      initiatedBy: 'Operator',
    }

    mockPendingState = {
      pendingApprovals: [testJob],
      pendingCount: 1,
      actionInProgressId: null,
      approveJob: mockApproveJob,
      rejectJob: mockRejectJob,
      approveAll: mockApproveAll,
    }

    const onClose = vi.fn()
    const onOpenCanvas = vi.fn()

    render(
      <QuickApprovalModal
        isOpen={true}
        onClose={onClose}
        onOpenCanvas={onOpenCanvas}
      />
    )

    expect(screen.getByText('Workflow Approval Gate')).toBeInTheDocument()
    expect(screen.getByText('node-alpha')).toBeInTheDocument()
    expect(screen.getByText('192.168.1.50')).toBeInTheDocument()
    expect(screen.getByText('Awaiting Operator Reboot Approval')).toBeInTheDocument()

    // View canvas link
    const canvasBtn = screen.getByRole('button', { name: /View Full DAG Canvas/i })
    fireEvent.click(canvasBtn)
    expect(onOpenCanvas).toHaveBeenCalledWith(testJob)
    expect(onClose).toHaveBeenCalled()

    // Approve button
    const approveBtn = screen.getByRole('button', { name: /Approve Reboot/i })
    fireEvent.click(approveBtn)
    expect(mockApproveJob).toHaveBeenCalledWith(testJob)
  })

  it('allows batch approve all when multiple jobs are pending', () => {
    const job1: JobSummary = {
      id: 'job-1',
      targetHostId: 'host-1',
      status: 'AwaitingApproval',
      initiatedBy: 'Operator',
    }
    const job2: JobSummary = {
      id: 'job-2',
      targetHostId: 'host-2',
      status: 'AwaitingApproval',
      initiatedBy: 'Operator',
    }

    mockPendingState = {
      pendingApprovals: [job1, job2],
      pendingCount: 2,
      actionInProgressId: null,
      approveJob: mockApproveJob,
      rejectJob: mockRejectJob,
      approveAll: mockApproveAll,
    }

    render(
      <QuickApprovalModal
        isOpen={true}
        onClose={vi.fn()}
      />
    )

    const approveAllBtn = screen.getByRole('button', { name: /Approve All \(2\)/i })
    expect(approveAllBtn).toBeInTheDocument()

    fireEvent.click(approveAllBtn)
    expect(mockApproveAll).toHaveBeenCalledTimes(1)
  })
})

import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RearmLicenseModal } from './RearmLicenseModal'
import * as hostsApi from '../../api/hosts'
import type { Host } from '../../api/hosts'

describe('RearmLicenseModal', () => {
  let queryClient: QueryClient

  const mockHost: Host = {
    id: '66666666-6666-6666-6666-666666666666',
    hostname: 'win-srv-01',
    ipAddress: '192.168.1.150',
    osFamily: 'windows',
    targetType: 'proxmox_vm',
    agent: {
      installed: true,
      isOnline: true,
      pendingReboot: false,
      upgradablePackagesCount: 0,
      version: '1.4.1',
    },
    windowsLicense: {
      isEvaluation: true,
      edition: 'Windows Server 2022 Datacenter Evaluation',
      licenseStatus: 'TIMEBASED_EVAL',
      gracePeriodMinutes: 7200,
      daysRemaining: 5,
      hoursRemaining: 120,
      expirationDate: new Date(Date.now() + 5 * 86400000).toISOString(),
      rearmsRemaining: 2,
      partialProductKey: 'DATAC',
      isExpiringSoon: true,
      isCritical: false,
      collectedAt: new Date().toISOString(),
    },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    vi.clearAllMocks()
  })

  it('renders modal with host license details when open is true', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <RearmLicenseModal host={mockHost} open={true} onClose={vi.fn()} />
      </QueryClientProvider>
    )

    expect(screen.getByText('Rearm Windows Evaluation License')).toBeInTheDocument()
    expect(screen.getByText(/win-srv-01/i)).toBeInTheDocument()
    expect(screen.getByText(/5 days remaining/i)).toBeInTheDocument()
    expect(screen.getByText(/2 rearms left/i)).toBeInTheDocument()
    expect(screen.getByText(/Projected: 1 left after rearm/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Rearm License/i })).not.toBeDisabled()
  })

  it('disables rearm button and shows warning banner when 0 rearms remaining', () => {
    const expiredHost: Host = {
      ...mockHost,
      windowsLicense: {
        ...mockHost.windowsLicense!,
        daysRemaining: 0,
        hoursRemaining: 0,
        rearmsRemaining: 0,
        isCritical: true,
      },
    }

    render(
      <QueryClientProvider client={queryClient}>
        <RearmLicenseModal host={expiredHost} open={true} onClose={vi.fn()} />
      </QueryClientProvider>
    )

    expect(screen.getByText(/Evaluation Rearm Limit Reached \(0 Rearms Left\)/i)).toBeInTheDocument()
    expect(screen.getByText(/slmgr\.vbs \/ipk/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Rearm License/i })).toBeDisabled()
  })

  it('submits rearm request with auto-reboot enabled', async () => {
    const rearmSpy = vi.spyOn(hostsApi, 'rearmWindowsLicense').mockResolvedValue({
      success: true,
      message: 'Windows license rearm Temporal workflow started successfully.',
      jobId: '11111111-2222-3333-4444-555555555555',
      rearmsRemaining: 1,
      daysRemaining: 180,
    })

    const onRearmSuccess = vi.fn()

    render(
      <QueryClientProvider client={queryClient}>
        <RearmLicenseModal
          host={mockHost}
          open={true}
          onClose={vi.fn()}
          onRearmSuccess={onRearmSuccess}
        />
      </QueryClientProvider>
    )

    const rearmButton = screen.getByRole('button', { name: /Rearm License/i })
    fireEvent.click(rearmButton)

    await waitFor(() => {
      expect(rearmSpy).toHaveBeenCalledWith('66666666-6666-6666-6666-666666666666', true)
    })

    expect(
      await screen.findByText(/Rearm workflow dispatched successfully!/i)
    ).toBeInTheDocument()
  })
})

import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { DemoResetModal } from './DemoResetModal'
import * as systemApi from '../../api/system'

describe('DemoResetModal', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    vi.clearAllMocks()
  })

  it('renders modal content when open is true', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <DemoResetModal open={true} onClose={vi.fn()} />
      </QueryClientProvider>
    )

    expect(screen.getByText('Reset Demo Environment')).toBeInTheDocument()
    expect(screen.getByText(/Restore simulated homelab fleet/i)).toBeInTheDocument()
    expect(screen.getByText(/pve-node-01/i)).toBeInTheDocument()
    expect(screen.getByText(/storage-nas-01/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Reset to Baseline/i })).toBeInTheDocument()
  })

  it('does not render when open is false', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <DemoResetModal open={false} onClose={vi.fn()} />
      </QueryClientProvider>
    )

    expect(screen.queryByText('Reset Demo Environment')).not.toBeInTheDocument()
  })

  it('calls resetDemoData and shows success message', async () => {
    const resetSpy = vi.spyOn(systemApi, 'resetDemoData').mockResolvedValue({
      message: 'Demo environment reset successfully.',
      hostCount: 5,
      timestamp: new Date().toISOString(),
    })

    const onClose = vi.fn()

    render(
      <QueryClientProvider client={queryClient}>
        <DemoResetModal open={true} onClose={onClose} />
      </QueryClientProvider>
    )

    const resetButton = screen.getByRole('button', { name: /Reset to Baseline/i })
    fireEvent.click(resetButton)

    await waitFor(() => {
      expect(resetSpy).toHaveBeenCalledTimes(1)
      expect(screen.getByText(/Reset complete! 5 demo hosts restored/i)).toBeInTheDocument()
    })
  })
})

import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { HardwareInventoryTab } from './HardwareInventoryTab'
import * as hostsApi from '../../api/hosts'

vi.mock('../../api/hosts', async () => {
  const actual = await vi.importActual('../../api/hosts')
  return {
    ...actual,
    fetchHostHardware: vi.fn(),
    triggerHardwareScan: vi.fn(),
  }
})

describe('HardwareInventoryTab', () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })

  it('renders hardware inventory with disks, SMART, wear-out, and PSUs', async () => {
    vi.mocked(hostsApi.fetchHostHardware).mockResolvedValueOnce({
      hostId: 'host-1',
      collectedAt: new Date().toISOString(),
      overallHealth: 'Ok',
      healthAlerts: [],
      source: 'agent',
      disks: [
        {
          deviceId: '/dev/nvme0n1',
          name: '/dev/nvme0n1',
          model: 'Samsung 980 PRO 1TB',
          serialNumber: 'S5GXNF0R123456',
          mediaType: 'NVMe',
          sizeBytes: 1000204886016,
          status: 'Ok',
          wearOutPercentage: 97.5,
          temperatureCelsius: 41,
          smartHealthStatus: 'PASSED',
        },
      ],
      controllers: [
        {
          id: 'c1',
          name: 'PERC H730P',
          status: 'Ok',
          batteryBackupHealthy: true,
          firmwareVersion: '25.5.9',
        },
      ],
      powerSupplies: [
        {
          id: 'psu1',
          name: 'PSU 1',
          status: 'Ok',
          outputWatts: 125,
          redundancyHealthy: true,
        },
      ],
      memoryModules: [
        {
          slotLocation: 'DIMM_A1',
          sizeBytes: 34359738368,
          speedMhz: '3200',
          status: 'Ok',
          correctableEccErrors: 0,
          uncorrectableEccErrors: 0,
        },
      ],
      zfsPools: [],
    })

    render(
      <QueryClientProvider client={queryClient}>
        <HardwareInventoryTab hostId="host-1" />
      </QueryClientProvider>
    )

    expect(await screen.findByText('Samsung 980 PRO 1TB')).toBeInTheDocument()
    expect(screen.getByText('SMART: PASSED')).toBeInTheDocument()
    expect(screen.getByText(/97.5% remaining life/i)).toBeInTheDocument()
    expect(screen.getByText('41°C')).toBeInTheDocument()
    expect(screen.getByText('PERC H730P')).toBeInTheDocument()
    expect(screen.getByText('BBU: Healthy')).toBeInTheDocument()
    expect(screen.getByText('PSU 1')).toBeInTheDocument()
    expect(screen.getByText('Redundant')).toBeInTheDocument()
    expect(screen.getByText('DIMM_A1')).toBeInTheDocument()
  })

  it('triggers on-demand hardware scan when clicking Scan Now', async () => {
    vi.mocked(hostsApi.fetchHostHardware).mockResolvedValueOnce({
      hostId: 'host-2',
      collectedAt: new Date().toISOString(),
      overallHealth: 'Ok',
      healthAlerts: [],
      disks: [],
      controllers: [],
      powerSupplies: [],
      memoryModules: [],
    })

    vi.mocked(hostsApi.triggerHardwareScan).mockResolvedValueOnce({
      hostId: 'host-2',
      collectedAt: new Date().toISOString(),
      overallHealth: 'Ok',
      healthAlerts: [],
      disks: [],
      controllers: [],
      powerSupplies: [],
      memoryModules: [],
    })

    render(
      <QueryClientProvider client={queryClient}>
        <HardwareInventoryTab hostId="host-2" />
      </QueryClientProvider>
    )

    const scanBtn = await screen.findByRole('button', { name: /scan now/i })
    fireEvent.click(scanBtn)

    await waitFor(() => {
      expect(hostsApi.triggerHardwareScan).toHaveBeenCalledWith('host-2')
    })
  })
})

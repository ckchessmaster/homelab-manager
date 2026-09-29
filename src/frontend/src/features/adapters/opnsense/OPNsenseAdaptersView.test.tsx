import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { OPNsenseAdaptersView } from './OPNsenseAdaptersView'
import * as opnsenseHooks from './useOPNsense'

vi.mock('./useOPNsense', async () => {
  const actual = await vi.importActual('./useOPNsense')
  return {
    ...actual,
    useOPNsenseInstances: vi.fn(),
    useDeleteOPNsenseInstance: vi.fn(),
    useOPNsenseTelemetry: vi.fn(),
    useOPNsenseDhcpLeases: vi.fn(),
    useOPNsenseVitals: vi.fn(),
    useOPNsenseFirmware: vi.fn(),
    useCheckOPNsenseFirmware: vi.fn(),
    useOPNsenseSecurityAlerts: vi.fn(),
    useOPNsenseFirewallStats: vi.fn(),
    useOPNsenseHAProxy: vi.fn(),
    useOPNsenseAcme: vi.fn(),
    useOPNsenseArp: vi.fn(),
    useRestartOPNsenseService: vi.fn(),
  }
})

describe('OPNsenseAdaptersView', () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  const mockInstance = {
    id: 'opnsense-main',
    name: 'Primary Gateway',
    baseUrl: 'https://192.168.1.1',
    description: 'Core Perimeter Router',
    firmwareVersion: '24.7.1',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    isHealthy: true,
    lastChecked: new Date().toISOString(),
    activeClientsCount: 42,
  }

  const mockTelemetry = {
    instanceId: 'opnsense-main',
    fetchedAt: new Date().toISOString(),
    system: {
      hostname: 'opnsense.lan',
      domain: 'home.arpa',
      version: '24.7.1',
      platform: 'FreeBSD 14.1-RELEASE',
      uptime: '14 days, 3 hours',
      cpuUsage: 12.5,
      memoryUsage: 35.8,
    },
    interfaces: [
      {
        name: 'wan',
        device: 'igb0',
        ipAddress: '198.51.100.2/24',
        status: 'up',
        description: 'WAN',
        macAddress: '00:08:a2:0c:12:34',
        mtu: '1500',
        media: '1000baseT <full-duplex>',
        enabled: true,
      },
      {
        name: 'lan',
        device: 'igb1',
        ipAddress: '192.168.1.1/24',
        status: 'up',
        description: 'LAN',
        macAddress: '00:08:a2:0c:12:35',
        mtu: '1500',
        media: '1000baseT <full-duplex>',
        enabled: true,
      },
    ],
    services: [
      { name: 'haproxy', description: 'HAProxy Load Balancer', running: true, enabled: true },
      { name: 'suricata', description: 'Suricata IDS/IPS', running: true, enabled: true },
    ],
    firmware: {
      currentVersion: '24.7.1',
      latestVersion: '24.7.3',
      updateAvailable: true,
      needsReboot: false,
      packagesToUpdate: 5,
      newPackagesCount: 0,
      reinstallPackagesCount: 0,
      removePackagesCount: 0,
      upgradeAction: 'upgrade',
      statusMsg: '5 packages can be updated',
      lastChecked: new Date().toISOString(),
    },
  }

  const mockVitals = {
    instanceId: 'opnsense-main',
    fetchedAt: new Date().toISOString(),
    loadAverage: [0.35, 0.42, 0.38],
    cpuUsagePercent: 12.5,
    cpuCount: 4,
    memoryTotalBytes: 8589934592,
    memoryUsedBytes: 3078496256,
    memoryUsagePercent: 35.8,
    diskTotalBytes: 64424509440,
    diskUsedBytes: 12884901888,
    diskUsagePercent: 20.0,
    temperaturesCelsius: [42.5, 41.0],
    uptimeString: '14 days, 3 hours',
    swapUsagePercent: 0,
  }

  const mockSecAlerts = {
    instanceId: 'opnsense-main',
    isInstalled: true,
    running: true,
    threatCount24h: 3,
    alerts: [
      {
        timestamp: '2026-09-29 10:00:00',
        severity: 'high',
        threat: 'ET SCAN Potential SSH Scan',
        category: 'Attempted Information Leak',
        sourceIp: '198.51.100.99',
        sourcePort: 54321,
        destinationIp: '192.168.1.1',
        destinationPort: 22,
        protocol: 'TCP',
        action: 'drop',
      },
    ],
  }

  const mockFwStats = {
    instanceId: 'opnsense-main',
    pfStatesCurrent: 1420,
    pfStatesMax: 600000,
    pfStatesPercent: 0.24,
    totalFilterRules: 42,
    totalAliases: 18,
    recentBlockedPacketsCount: 14,
  }

  beforeEach(() => {
    vi.mocked(opnsenseHooks.useOPNsenseInstances).mockReturnValue({
      data: [mockInstance],
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useDeleteOPNsenseInstance).mockReturnValue({
      mutateAsync: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseTelemetry).mockReturnValue({
      data: mockTelemetry,
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseDhcpLeases).mockReturnValue({
      data: [],
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseVitals).mockReturnValue({
      data: mockVitals,
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseFirmware).mockReturnValue({
      data: {
        version: '24.7.1',
        updatesAvailable: 5,
        lastCheck: new Date().toISOString(),
        needsReboot: false,
        statusMsg: '5 packages can be updated',
        upgradeAction: 'upgrade',
      },
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useCheckOPNsenseFirmware).mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseSecurityAlerts).mockReturnValue({
      data: mockSecAlerts,
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseFirewallStats).mockReturnValue({
      data: mockFwStats,
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseHAProxy).mockReturnValue({
      data: { isInstalled: false, frontendsCount: 0, backendsCount: 0, servers: [] },
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseAcme).mockReturnValue({
      data: { isInstalled: false, certificatesCount: 0, certificates: [] },
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseArp).mockReturnValue({
      data: [],
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useRestartOPNsenseService).mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as any)
  })

  it('renders all 4 tabs and displays overview vitals and interfaces', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <OPNsenseAdaptersView />
      </QueryClientProvider>
    )

    // Check tabs
    expect(screen.getByText('Overview & Vitals')).toBeInTheDocument()
    expect(screen.getByText('Firewall & Security')).toBeInTheDocument()
    expect(screen.getByText('Services & Proxy')).toBeInTheDocument()
    expect(screen.getByText('DHCP & Network')).toBeInTheDocument()

    // Check firmware banner
    expect(screen.getByText(/5 Updates Available/i)).toBeInTheDocument()
    expect(screen.getByText(/24\.7\.1/i)).toBeInTheDocument()

    // Check interfaces table
    expect(screen.getByText('[WAN]')).toBeInTheDocument()
    expect(screen.getByText('[LAN]')).toBeInTheDocument()
  })

  it('switches to Firewall & Security tab when clicked', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <OPNsenseAdaptersView />
      </QueryClientProvider>
    )

    const secTab = screen.getByText('Firewall & Security')
    fireEvent.click(secTab)

    expect(screen.getByText(/Active PF States/i)).toBeInTheDocument()
    expect(screen.getByText(/Suricata Intrusion Detection & Security Alerts/i)).toBeInTheDocument()
    expect(screen.getByText(/ET SCAN Potential SSH Scan/i)).toBeInTheDocument()
  })
})

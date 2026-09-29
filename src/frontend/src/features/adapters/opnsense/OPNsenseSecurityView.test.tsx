import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { OPNsenseSecurityView } from './OPNsenseSecurityView'
import * as opnsenseHooks from './useOPNsense'

vi.mock('./useOPNsense', () => ({
  useOPNsenseSecurityAlerts: vi.fn(),
  useOPNsenseFirewallStats: vi.fn(),
}))

describe('OPNsenseSecurityView', () => {
  const mockAlerts = [
    {
      timestamp: '2026-09-29 10:00:00',
      severity: 'high',
      threat: 'ET SCAN Suspicious Inbound Port 22 Scan',
      category: 'Network Scan',
      sourceIp: '198.51.100.44',
      sourcePort: 51234,
      destinationIp: '198.51.100.1',
      destinationPort: 22,
      protocol: 'TCP',
      action: 'drop',
    },
    {
      timestamp: '2026-09-29 09:30:00',
      severity: 'high',
      threat: 'GPL ATTACK_RESPONSE id check returned root',
      category: 'Attempted Admin Privilege Gain',
      sourceIp: '203.0.113.89',
      sourcePort: 443,
      destinationIp: '10.10.30.50',
      destinationPort: 54321,
      protocol: 'TCP',
      action: 'alert',
    },
  ]

  const mockSecStatus = {
    instanceId: 'opnsense-1',
    isInstalled: true,
    running: true,
    threatCount24h: 14,
    alerts: mockAlerts,
  }

  const mockFwStats = {
    instanceId: 'opnsense-1',
    pfStatesCurrent: 1420,
    pfStatesMax: 600000,
    pfStatesPercent: 0.24,
    totalFilterRules: 42,
    totalAliases: 18,
    recentBlockedPacketsCount: 52,
  }

  beforeEach(() => {
    localStorage.clear()
    vi.mocked(opnsenseHooks.useOPNsenseSecurityAlerts).mockReturnValue({
      data: mockSecStatus,
      isLoading: false,
      refetch: vi.fn(),
    } as any)

    vi.mocked(opnsenseHooks.useOPNsenseFirewallStats).mockReturnValue({
      data: mockFwStats,
      isLoading: false,
      refetch: vi.fn(),
    } as any)
  })

  it('renders security metric strip and alert table without adopt buttons', () => {
    render(<OPNsenseSecurityView instanceId="opnsense-1" />)

    expect(screen.getByText(/Active PF States/i)).toBeInTheDocument()
    expect(screen.getByText('1,420')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
    expect(screen.getByText('18')).toBeInTheDocument()

    // Alert threats rendered
    expect(screen.getByText('ET SCAN Suspicious Inbound Port 22 Scan')).toBeInTheDocument()
    expect(screen.getByText('GPL ATTACK_RESPONSE id check returned root')).toBeInTheDocument()

    // Ensure NO Adopt buttons exist on this page
    expect(screen.queryByTitle(/Adopt Destination Host/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Adopt Node/i)).not.toBeInTheDocument()
  })

  it('allows dismissing an alert instance and restoring it', () => {
    render(<OPNsenseSecurityView instanceId="opnsense-1" />)

    const dismissButtons = screen.getAllByRole('button', { name: /Dismiss/i })
    expect(dismissButtons.length).toBe(2)

    // Dismiss first alert
    fireEvent.click(dismissButtons[0])

    // First alert should no longer be visible
    expect(screen.queryByText('ET SCAN Suspicious Inbound Port 22 Scan')).not.toBeInTheDocument()
    expect(screen.getByText('GPL ATTACK_RESPONSE id check returned root')).toBeInTheDocument()

    // Restore button appears
    const restoreButton = screen.getByRole('button', { name: /Restore \(1\)/i })
    expect(restoreButton).toBeInTheDocument()

    // Click restore
    fireEvent.click(restoreButton)
    expect(screen.getByText('ET SCAN Suspicious Inbound Port 22 Scan')).toBeInTheDocument()
  })

  it('allows ignoring a threat signature going forward and unignoring it via modal', () => {
    render(<OPNsenseSecurityView instanceId="opnsense-1" />)

    const ignoreButtons = screen.getAllByRole('button', { name: /Ignore Rule/i })
    expect(ignoreButtons.length).toBe(2)

    // Ignore the port scan rule
    fireEvent.click(ignoreButtons[0])

    // Alert is immediately hidden
    expect(screen.queryByText('ET SCAN Suspicious Inbound Port 22 Scan')).not.toBeInTheDocument()

    // "1 Ignored Rule" button appears
    const ignoredRuleBtn = screen.getByRole('button', { name: /1 Ignored Rule/i })
    expect(ignoredRuleBtn).toBeInTheDocument()

    // Open modal
    fireEvent.click(ignoredRuleBtn)
    expect(screen.getByText('Ignored Alert Rules & Signatures')).toBeInTheDocument()

    // Unignore / restore rule
    const restoreThreatBtn = screen.getByRole('button', { name: /^Restore$/i })
    fireEvent.click(restoreThreatBtn)

    // Close modal
    const doneBtn = screen.getByRole('button', { name: /Done/i })
    fireEvent.click(doneBtn)

    // Alert is visible again
    expect(screen.getByText('ET SCAN Suspicious Inbound Port 22 Scan')).toBeInTheDocument()
  })
})

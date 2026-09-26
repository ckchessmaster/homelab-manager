import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { HostMobileCardList } from './HostMobileCardList'
import type { Host } from '../../api/hosts'

describe('HostMobileCardList', () => {
  const mockHost: Host = {
    id: 'host-1',
    hostname: 'k8s-node-01',
    friendlyName: 'Kubernetes Worker',
    ipAddress: '192.168.1.100',
    osFamily: 'linux_ubuntu',
    targetType: 'baremetal',
    agent: {
      installed: true,
      version: '1.2.0',
      lastSeenAt: new Date().toISOString(),
      pendingReboot: true,
      upgradablePackagesCount: 5,
    },
    createdAt: '2026-01-01',
    updatedAt: '2026-01-01',
  }

  it('renders host cards with hostname, friendly name, IP and badges', () => {
    const onInspect = vi.fn()
    const onToggleSelect = vi.fn()

    render(
      <HostMobileCardList
        hosts={[mockHost]}
        selectedHostIds={new Set()}
        onToggleSelect={onToggleSelect}
        onInspect={onInspect}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onReboot={vi.fn()}
        onOpenTerminal={vi.fn()}
        onTriggerUpdate={vi.fn()}
        onOpenSnapshots={vi.fn()}
        onViewDag={vi.fn()}
        activeJobsByHost={new Map()}
        copiedIp={null}
        onCopyIp={vi.fn()}
        isAdmin={true}
        isOperator={true}
      />
    )

    expect(screen.getByText('k8s-node-01')).toBeInTheDocument()
    expect(screen.getByText('Kubernetes Worker')).toBeInTheDocument()
    expect(screen.getByText('192.168.1.100')).toBeInTheDocument()
    expect(screen.getByText(/Ubuntu/i)).toBeInTheDocument()
    expect(screen.getByText(/Reboot/i)).toBeInTheDocument()
  })

  it('triggers onInspect when card is clicked', () => {
    const onInspect = vi.fn()

    render(
      <HostMobileCardList
        hosts={[mockHost]}
        selectedHostIds={new Set()}
        onToggleSelect={vi.fn()}
        onInspect={onInspect}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onReboot={vi.fn()}
        onOpenTerminal={vi.fn()}
        onTriggerUpdate={vi.fn()}
        onOpenSnapshots={vi.fn()}
        onViewDag={vi.fn()}
        activeJobsByHost={new Map()}
        copiedIp={null}
        onCopyIp={vi.fn()}
        isAdmin={true}
        isOperator={true}
      />
    )

    fireEvent.click(screen.getByText('k8s-node-01'))
    expect(onInspect).toHaveBeenCalledWith(mockHost)
  })

  it('toggles selection when checkbox is clicked without triggering card inspect', () => {
    const onInspect = vi.fn()
    const onToggleSelect = vi.fn()

    render(
      <HostMobileCardList
        hosts={[mockHost]}
        selectedHostIds={new Set()}
        onToggleSelect={onToggleSelect}
        onInspect={onInspect}
        onEdit={vi.fn()}
        onDelete={vi.fn()}
        onReboot={vi.fn()}
        onOpenTerminal={vi.fn()}
        onTriggerUpdate={vi.fn()}
        onOpenSnapshots={vi.fn()}
        onViewDag={vi.fn()}
        activeJobsByHost={new Map()}
        copiedIp={null}
        onCopyIp={vi.fn()}
        isAdmin={true}
        isOperator={true}
      />
    )

    const checkbox = screen.getByRole('checkbox', { name: /Select k8s-node-01/i })
    fireEvent.click(checkbox)
    expect(onToggleSelect).toHaveBeenCalledWith('host-1')
    expect(onInspect).not.toHaveBeenCalled()
  })
})

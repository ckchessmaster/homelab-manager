import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { OPNsenseInterfacesTable } from './OPNsenseInterfacesTable'
import type { OPNsenseInterface } from '../../../api/opnsense'

describe('OPNsenseInterfacesTable', () => {
  const mockInterfaces: OPNsenseInterface[] = [
    {
      name: 'wan',
      device: 'igb0',
      ipAddress: '198.51.100.2/24',
      status: 'up',
      description: 'WAN',
      macAddress: '00:08:a2:0c:12:34',
      mtu: 1500,
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
      mtu: 1500,
      media: '1000baseT <full-duplex>',
      enabled: true,
    },
    {
      name: 'opt1',
      device: 'igb1.20',
      ipAddress: '10.0.20.1/24',
      status: 'up',
      description: 'KUBERNETES',
      macAddress: '00:08:a2:0c:12:35',
      mtu: 1500,
      media: '1000baseT <full-duplex>',
      enabled: true,
    },
    {
      name: 'opt2',
      device: 'igb2',
      ipAddress: '',
      status: 'down',
      description: 'BACKUP_LINK',
      macAddress: '00:08:a2:0c:12:36',
      mtu: 1500,
      media: 'no carrier',
      enabled: false,
    },
  ]

  it('renders all interfaces with appropriate zone badges and metadata', () => {
    render(<OPNsenseInterfacesTable interfaces={mockInterfaces} />)

    expect(screen.getByText(/Physical & Virtual Interfaces \(4\)/i)).toBeInTheDocument()

    // Zone badges
    expect(screen.getByText('[WAN]')).toBeInTheDocument()
    expect(screen.getByText('[LAN]')).toBeInTheDocument()
    expect(screen.getByText('[KUBERNETES]')).toBeInTheDocument()
    expect(screen.getByText('[BACKUP_LINK]')).toBeInTheDocument()

    // Devices & IPs
    expect(screen.getByText('igb0')).toBeInTheDocument()
    expect(screen.getByText('198.51.100.2/24')).toBeInTheDocument()
    expect(screen.getByText('192.168.1.1/24')).toBeInTheDocument()
    expect(screen.getByText('10.0.20.1/24')).toBeInTheDocument()

    // Unassigned IP message
    expect(screen.getByText(/Unassigned \/ Bridge/i)).toBeInTheDocument()
  })

  it('filters interfaces based on search query', () => {
    render(<OPNsenseInterfacesTable interfaces={mockInterfaces} />)

    const searchInput = screen.getByPlaceholderText(/Filter interfaces/i)
    fireEvent.change(searchInput, { target: { value: 'KUBERNETES' } })

    expect(screen.getByText('[KUBERNETES]')).toBeInTheDocument()
    expect(screen.queryByText('[WAN]')).not.toBeInTheDocument()
    expect(screen.queryByText('[LAN]')).not.toBeInTheDocument()
  })

  it('copies MAC address to clipboard when clicked', () => {
    Object.assign(navigator, {
      clipboard: {
        writeText: vi.fn(),
      },
    })

    render(<OPNsenseInterfacesTable interfaces={mockInterfaces} />)

    const copyButtons = screen.getAllByTitle('Copy MAC')
    expect(copyButtons.length).toBeGreaterThan(0)
    fireEvent.click(copyButtons[0])

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('00:08:a2:0c:12:34')
  })

  it('displays empty state when interfaces array is empty', () => {
    render(<OPNsenseInterfacesTable interfaces={[]} />)
    expect(screen.getByText(/No interface data available/i)).toBeInTheDocument()
  })
})

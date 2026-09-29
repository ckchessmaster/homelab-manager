import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { OsBadge, AgentStatusBadge, RebootBadge, UpdatesBadge, TargetTypeBadge, HardwareHealthBadge } from './HostStatusBadge'

describe('HostStatusBadge Primitives', () => {
  it('renders OsBadge for different OS families', () => {
    const { rerender } = render(<OsBadge osFamily="linux_debian" />)
    expect(screen.getByText('Debian')).toBeInTheDocument()

    rerender(<OsBadge osFamily="linux_ubuntu" />)
    expect(screen.getByText('Ubuntu')).toBeInTheDocument()

    rerender(<OsBadge osFamily="windows" />)
    expect(screen.getByText('Windows')).toBeInTheDocument()
  })

  it('renders TargetTypeBadge correctly', () => {
    const { rerender } = render(<TargetTypeBadge type="baremetal" />)
    expect(screen.getByText('Bare-Metal')).toBeInTheDocument()

    rerender(<TargetTypeBadge type="proxmox_qemu" />)
    expect(screen.getByText('Proxmox VM')).toBeInTheDocument()

    rerender(<TargetTypeBadge type="proxmox_lxc" />)
    expect(screen.getByText('Proxmox LXC')).toBeInTheDocument()
  })

  it('renders AgentStatusBadge online with version and outdated notification', () => {
    render(
      <AgentStatusBadge
        agent={{
          installed: true,
          version: '1.2.0',
          lastSeenAt: new Date().toISOString(),
          pendingReboot: false,
          upgradablePackagesCount: 0,
        }}
        targetVersion="1.4.1"
      />
    )

    expect(screen.getByText(/Online/i)).toBeInTheDocument()
    expect(screen.getByText(/v1\.4\.1 avail/i)).toBeInTheDocument()
  })

  it('renders RebootBadge only when pending is true', () => {
    const { rerender } = render(<RebootBadge pending={false} />)
    expect(screen.queryByText(/Reboot Pending/i)).not.toBeInTheDocument()

    rerender(<RebootBadge pending={true} />)
    expect(screen.getByText(/Reboot Pending/i)).toBeInTheDocument()
  })

  it('renders UpdatesBadge with proper pluralization', () => {
    const { rerender } = render(<UpdatesBadge count={1} />)
    expect(screen.getByText('1 update')).toBeInTheDocument()

    rerender(<UpdatesBadge count={4} />)
    expect(screen.getByText('4 updates')).toBeInTheDocument()

    rerender(<UpdatesBadge count={0} />)
    expect(screen.queryByText(/update/i)).not.toBeInTheDocument()
  })

  it('renders HardwareHealthBadge for Ok, Warning, and Critical', () => {
    const { rerender } = render(<HardwareHealthBadge status="Ok" />)
    expect(screen.getByText('Hardware OK')).toBeInTheDocument()

    rerender(<HardwareHealthBadge status="Warning" alerts={['Disk /dev/sda elevated temp: 58°C']} />)
    expect(screen.getByText(/Hardware Warning/i)).toBeInTheDocument()
    expect(screen.getByText(/1/i)).toBeInTheDocument()

    rerender(<HardwareHealthBadge status="Critical" alerts={['Disk /dev/sda SMART failed', 'PSU redundancy lost']} />)
    expect(screen.getByText(/Hardware Critical/i)).toBeInTheDocument()
    expect(screen.getByText(/2/i)).toBeInTheDocument()

    rerender(<HardwareHealthBadge status={null} />)
    expect(screen.queryByText(/Hardware/i)).not.toBeInTheDocument()
  })
})

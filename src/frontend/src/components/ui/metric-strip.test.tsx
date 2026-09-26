import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { MetricStrip } from './metric-strip'
import { Server } from 'lucide-react'

describe('MetricStrip', () => {
  const items = [
    {
      id: 'nodes',
      label: 'Nodes',
      value: 12,
      icon: Server,
      badge: 'Active',
      badgeVariant: 'success' as const,
    },
    {
      id: 'reboot',
      label: 'Reboot',
      value: 2,
      badge: 'Pending',
      badgeVariant: 'warning' as const,
      onClick: vi.fn(),
    },
  ]

  it('renders metric labels, values, and badges correctly', () => {
    render(<MetricStrip items={items} />)

    expect(screen.getByText('Nodes:')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()

    expect(screen.getByText('Reboot:')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('Pending')).toBeInTheDocument()
  })

  it('triggers onClick handler when clicking an actionable metric item', () => {
    render(<MetricStrip items={items} />)

    const clickableItem = screen.getByText('Reboot:').closest('div')
    expect(clickableItem).not.toBeNull()
    fireEvent.click(clickableItem!)

    expect(items[1].onClick).toHaveBeenCalledTimes(1)
  })
})

import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { MobileBottomNav } from './MobileBottomNav'

describe('MobileBottomNav', () => {
  it('renders primary navigation tabs and more button', () => {
    const onSelectTab = vi.fn()
    render(
      <MobileBottomNav
        activeTab="hosts"
        onSelectTab={onSelectTab}
        totalHosts={5}
        rebootPendingCount={1}
      />
    )

    expect(screen.getByRole('button', { name: 'Hosts' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Workloads' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Discovery' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Workflows' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /More views and options/i })).toBeInTheDocument()
  })

  it('calls onSelectTab when a primary tab is clicked', () => {
    const onSelectTab = vi.fn()
    render(
      <MobileBottomNav
        activeTab="hosts"
        onSelectTab={onSelectTab}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Workloads' }))
    expect(onSelectTab).toHaveBeenCalledWith('workloads')
  })

  it('opens More sheet when More button is clicked', () => {
    const onSelectTab = vi.fn()
    render(
      <MobileBottomNav
        activeTab="hosts"
        onSelectTab={onSelectTab}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /More views and options/i }))
    expect(screen.getByText('ControlPlane Navigation')).toBeInTheDocument()
    expect(screen.getByText('Infrastructure Adapters')).toBeInTheDocument()
    expect(screen.getByText('System & Settings')).toBeInTheDocument()
  })

  it('navigates to adapters from More sheet and closes it', () => {
    const onSelectTab = vi.fn()
    render(
      <MobileBottomNav
        activeTab="hosts"
        onSelectTab={onSelectTab}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /More views and options/i }))
    fireEvent.click(screen.getByText('Infrastructure Adapters'))
    expect(onSelectTab).toHaveBeenCalledWith('adapters')
  })
})

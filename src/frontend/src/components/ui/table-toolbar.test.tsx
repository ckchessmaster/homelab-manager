import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import {
  TableToolbar,
  TableToolbarSearch,
  TableToolbarSegment,
} from './table-toolbar'

describe('TableToolbar', () => {
  it('renders search input and allows typing and clearing', () => {
    const onChange = vi.fn()
    const { rerender } = render(
      <TableToolbar>
        <TableToolbarSearch value="k8s" onChange={onChange} placeholder="Search..." />
      </TableToolbar>
    )

    const input = screen.getByPlaceholderText('Search...')
    expect(input).toHaveValue('k8s')

    const clearBtn = screen.getByRole('button', { name: /Clear search/i })
    expect(clearBtn).toBeInTheDocument()
    fireEvent.click(clearBtn)
    expect(onChange).toHaveBeenCalledWith('')

    rerender(
      <TableToolbar>
        <TableToolbarSearch value="" onChange={onChange} placeholder="Search..." />
      </TableToolbar>
    )
    expect(screen.queryByRole('button', { name: /Clear search/i })).not.toBeInTheDocument()
  })

  it('renders segment buttons and triggers onChange on selection', () => {
    const onChange = vi.fn()
    const options = [
      { id: 'all', label: 'All Items' },
      { id: 'active', label: 'Active Only' },
    ]

    render(
      <TableToolbarSegment
        options={options}
        value="all"
        onChange={onChange}
      />
    )

    expect(screen.getByText('All Items')).toBeInTheDocument()
    const activeBtn = screen.getByText('Active Only')
    fireEvent.click(activeBtn)
    expect(onChange).toHaveBeenCalledWith('active')
  })
})

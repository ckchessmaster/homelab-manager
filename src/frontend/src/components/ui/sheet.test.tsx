import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { Sheet, SheetHeader, SheetTitle, SheetBody, SheetFooter } from './sheet'

describe('Sheet', () => {
  it('renders title, body, and footer when open is true', () => {
    const onClose = vi.fn()
    render(
      <Sheet open={true} onClose={onClose}>
        <SheetHeader onClose={onClose}>
          <SheetTitle>Host Details</SheetTitle>
        </SheetHeader>
        <SheetBody>
          <p>Body Content</p>
        </SheetBody>
        <SheetFooter>
          <button type="button">Save</button>
        </SheetFooter>
      </Sheet>
    )

    expect(screen.getByText('Host Details')).toBeInTheDocument()
    expect(screen.getByText('Body Content')).toBeInTheDocument()
    expect(screen.getByText('Save')).toBeInTheDocument()
  })

  it('does not render content when open is false', () => {
    const onClose = vi.fn()
    render(
      <Sheet open={false} onClose={onClose}>
        <div>Hidden</div>
      </Sheet>
    )

    expect(screen.queryByText('Hidden')).not.toBeInTheDocument()
  })

  it('triggers onClose when close button is clicked', () => {
    const onClose = vi.fn()
    render(
      <Sheet open={true} onClose={onClose}>
        <SheetHeader onClose={onClose}>
          <SheetTitle>Details</SheetTitle>
        </SheetHeader>
      </Sheet>
    )

    const closeBtn = screen.getByRole('button', { name: /Close dialog/i })
    fireEvent.click(closeBtn)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('triggers onClose when Escape key is pressed', () => {
    const onClose = vi.fn()
    render(
      <Sheet open={true} onClose={onClose}>
        <div>Modal Content</div>
      </Sheet>
    )

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

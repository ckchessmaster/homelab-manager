import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'

interface DropdownMenuContextType {
  isOpen: boolean
  setIsOpen: React.Dispatch<React.SetStateAction<boolean>>
  closeMenu: () => void
  triggerElement: HTMLElement | null
  contentElement: HTMLElement | null
  setTriggerElement: (el: HTMLElement | null) => void
  setContentElement: (el: HTMLElement | null) => void
}

const DropdownMenuContext = createContext<DropdownMenuContextType | null>(null)

export function DropdownMenu({ children }: { children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false)
  const [triggerElement, setTriggerElement] = useState<HTMLElement | null>(null)
  const [contentElement, setContentElement] = useState<HTMLElement | null>(null)

  const closeMenu = () => setIsOpen(false)

  // Close on outside click
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      const target = event.target as Node
      const isTriggerClick = triggerElement?.contains(target)
      const isContentClick = contentElement?.contains(target)

      if (!isTriggerClick && !isContentClick) {
        setIsOpen(false)
      }
    }

    // Close on Escape
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
      }
    }

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside)
      document.addEventListener('keydown', handleKeyDown)
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [isOpen, triggerElement, contentElement])

  return (
    <DropdownMenuContext.Provider
      value={{
        isOpen,
        setIsOpen,
        closeMenu,
        triggerElement,
        contentElement,
        setTriggerElement,
        setContentElement,
      }}
    >
      <div className="relative inline-block text-left">{children}</div>
    </DropdownMenuContext.Provider>
  )
}

export function DropdownMenuTrigger({
  children,
  asChild = false,
  className = '',
}: {
  children: ReactNode
  asChild?: boolean
  className?: string
}) {
  const ctx = useContext(DropdownMenuContext)
  if (!ctx) throw new Error('DropdownMenuTrigger must be inside DropdownMenu')

  const { isOpen, setIsOpen, setTriggerElement } = ctx

  const toggle = (e: React.MouseEvent) => {
    e.stopPropagation()
    setIsOpen((prev) => !prev)
  }

  if (asChild && React.isValidElement(children)) {
    return React.cloneElement(children as React.ReactElement<any>, {
      ref: setTriggerElement,
      onClick: (e: React.MouseEvent) => {
        toggle(e)
        const existingOnClick = (children.props as any)?.onClick
        if (typeof existingOnClick === 'function') {
          existingOnClick(e)
        }
      },
      'aria-haspopup': 'menu',
      'aria-expanded': isOpen,
    })
  }

  return (
    <button
      ref={setTriggerElement}
      type="button"
      onClick={toggle}
      aria-haspopup="menu"
      aria-expanded={isOpen}
      className={className}
    >
      {children}
    </button>
  )
}

export function DropdownMenuContent({
  children,
  align = 'right',
  className = '',
}: {
  children: ReactNode
  align?: 'left' | 'right'
  className?: string
}) {
  const ctx = useContext(DropdownMenuContext)
  if (!ctx) throw new Error('DropdownMenuContent must be inside DropdownMenu')

  const [coords, setCoords] = useState<{ top: number; left?: number; right?: number } | null>(null)

  const { isOpen, triggerElement, contentElement, setContentElement } = ctx

  useEffect(() => {
    if (!isOpen || !triggerElement) return

    const updatePosition = () => {
      const rect = triggerElement.getBoundingClientRect()
      const spaceBelow = window.innerHeight - rect.bottom
      const estimatedHeight = contentElement?.offsetHeight || 260
      const openUpwards = spaceBelow < estimatedHeight && rect.top > estimatedHeight

      const top = openUpwards ? rect.top - estimatedHeight - 4 : rect.bottom + 4

      if (align === 'right') {
        const right = Math.max(8, window.innerWidth - rect.right)
        setCoords({ top, right })
      } else {
        const left = Math.max(8, rect.left)
        setCoords({ top, left })
      }
    }

    updatePosition()
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)

    return () => {
      window.removeEventListener('resize', updatePosition)
      window.removeEventListener('scroll', updatePosition, true)
    }
  }, [isOpen, align, triggerElement, contentElement])

  if (!isOpen) return null

  const content = (
    <div
      ref={setContentElement}
      role="menu"
      style={{
        position: 'fixed',
        top: coords?.top ?? 0,
        ...(coords?.right !== undefined ? { right: coords.right } : {}),
        ...(coords?.left !== undefined ? { left: coords.left } : {}),
        zIndex: 9999,
      }}
      className={`min-w-[200px] rounded-xl border border-zinc-800 bg-zinc-950/98 p-1 text-zinc-200 shadow-2xl backdrop-blur-xl animate-in fade-in zoom-in-95 duration-100 ${className}`}
      onClick={(e) => e.stopPropagation()}
    >
      {children}
    </div>
  )

  return createPortal(content, document.body)
}

export function DropdownMenuItem({
  children,
  onClick,
  disabled = false,
  destructive = false,
  className = '',
}: {
  children: ReactNode
  onClick?: (e: React.MouseEvent) => void
  disabled?: boolean
  destructive?: boolean
  className?: string
}) {
  const ctx = useContext(DropdownMenuContext)

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (disabled) return
    ctx?.closeMenu()
    onClick?.(e)
  }

  const destructiveStyles = destructive
    ? 'text-rose-400 hover:text-rose-200 hover:bg-rose-950/40 focus:bg-rose-950/40'
    : 'text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800/80 focus:bg-zinc-800/80'

  return (
    <button
      type="button"
      role="menuitem"
      disabled={disabled}
      onClick={handleClick}
      className={`relative flex w-full cursor-pointer select-none items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-xs font-medium outline-none transition-colors ${destructiveStyles} ${
        disabled ? 'cursor-not-allowed opacity-40 hover:bg-transparent hover:text-inherit' : ''
      } ${className}`}
    >
      {children}
    </button>
  )
}

export function DropdownMenuSeparator({ className = '' }: { className?: string }) {
  return <div role="separator" className={`my-1 h-px bg-zinc-800/70 ${className}`} />
}

export function DropdownMenuLabel({
  children,
  className = '',
}: {
  children: ReactNode
  className?: string
}) {
  return (
    <div
      className={`px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider text-zinc-500 ${className}`}
    >
      {children}
    </div>
  )
}

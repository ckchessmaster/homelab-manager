import React, { useEffect } from 'react'
import { X } from 'lucide-react'
import { cn } from '../../lib/utils'

export interface SheetProps {
  open: boolean
  onClose: () => void
  children: React.ReactNode
  width?: string
  className?: string
}

export function Sheet({
  open,
  onClose,
  children,
  width = 'sm:w-[560px]',
  className = '',
}: SheetProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose()
      }
    }
    if (open) {
      document.body.style.overflow = 'hidden'
      window.addEventListener('keydown', handleKeyDown)
    }
    return () => {
      document.body.style.overflow = ''
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/70 backdrop-blur-xs transition-opacity animate-in fade-in duration-200"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Slide-over Sheet Panel */}
      <div
        role="dialog"
        aria-modal="true"
        className={cn(
          'relative w-full h-full bg-zinc-900 border-l border-zinc-800 shadow-2xl z-10 flex flex-col animate-in slide-in-from-right duration-250 ease-out',
          width,
          className
        )}
      >
        {children}
      </div>
    </div>
  )
}

export function SheetHeader({
  children,
  onClose,
  className,
}: {
  children: React.ReactNode
  onClose?: () => void
  className?: string
}) {
  return (
    <div
      className={cn(
        'px-5 py-4 border-b border-zinc-800 flex items-center justify-between gap-3 shrink-0 bg-zinc-950/40',
        className
      )}
    >
      <div className="flex-1 min-w-0">{children}</div>
      {onClose && (
        <button
          onClick={onClose}
          type="button"
          className="text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 p-1.5 rounded-lg transition-colors cursor-pointer shrink-0"
          title="Close (Esc)"
        >
          <X className="h-4 w-4" />
        </button>
      )}
    </div>
  )
}

export function SheetTitle({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <h2 className={cn('text-base font-semibold text-zinc-100 truncate', className)}>
      {children}
    </h2>
  )
}

export function SheetDescription({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <p className={cn('text-xs text-zinc-400 mt-0.5', className)}>
      {children}
    </p>
  )
}

export function SheetBody({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'px-5 py-5 overflow-y-auto flex-1 space-y-5 scrollbar-thin scrollbar-thumb-zinc-800 scrollbar-track-transparent',
        className
      )}
    >
      {children}
    </div>
  )
}

export function SheetFooter({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'px-5 py-3.5 border-t border-zinc-800 bg-zinc-950/80 backdrop-blur-md flex items-center justify-end gap-2.5 shrink-0 sticky bottom-0 z-10',
        className
      )}
    >
      {children}
    </div>
  )
}

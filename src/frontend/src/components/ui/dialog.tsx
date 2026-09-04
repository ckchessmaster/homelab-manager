import React, { useEffect } from 'react'
import { X } from 'lucide-react'
import { cn } from '../../lib/utils'

export interface DialogProps {
  open: boolean
  onClose: () => void
  children: React.ReactNode
  maxWidth?: 'sm' | 'md' | 'lg' | 'xl' | '2xl'
}

export function Dialog({ open, onClose, children, maxWidth = 'md' }: DialogProps) {
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

  const maxWidths = {
    sm: 'max-w-sm',
    md: 'max-w-md',
    lg: 'max-w-lg',
    xl: 'max-w-xl',
    '2xl': 'max-w-2xl',
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/75 backdrop-blur-sm transition-opacity animate-in fade-in"
        onClick={onClose}
      />

      {/* Modal Dialog Card */}
      <div
        role="dialog"
        aria-modal="true"
        className={cn(
          'relative w-full bg-zinc-900 border border-zinc-800 rounded-xl shadow-2xl overflow-hidden transition-all transform z-10 animate-in zoom-in-95 max-h-[90vh] flex flex-col',
          maxWidths[maxWidth]
        )}
      >
        {children}
      </div>
    </div>
  )
}

export function DialogHeader({
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
        'px-6 py-4 border-b border-zinc-800 flex items-center justify-between bg-zinc-900/50',
        className
      )}
    >
      <div>{children}</div>
      {onClose && (
        <button
          onClick={onClose}
          className="text-zinc-400 hover:text-zinc-100 p-1.5 rounded-lg hover:bg-zinc-800/80 transition-colors"
          aria-label="Close dialog"
        >
          <X className="h-4 w-4" />
        </button>
      )}
    </div>
  )
}

export function DialogTitle({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <h2 className={cn('text-lg font-semibold text-zinc-100 leading-none tracking-tight', className)}>
      {children}
    </h2>
  )
}

export function DialogDescription({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <p className={cn('text-xs text-zinc-400 mt-1.5', className)}>
      {children}
    </p>
  )
}

export function DialogBody({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div className={cn('px-6 py-5 overflow-y-auto space-y-4 flex-1 text-sm text-zinc-200', className)}>
      {children}
    </div>
  )
}

export function DialogFooter({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'px-6 py-3.5 bg-zinc-950/60 border-t border-zinc-800/80 flex items-center justify-end gap-3',
        className
      )}
    >
      {children}
    </div>
  )
}

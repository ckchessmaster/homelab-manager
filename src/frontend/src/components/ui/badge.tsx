import React from 'react'
import { cn } from '../../lib/utils'

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: 'default' | 'success' | 'warning' | 'destructive' | 'info' | 'outline' | 'purple'
  dot?: boolean
  pulse?: boolean
}

export function Badge({
  className,
  variant = 'default',
  dot = false,
  pulse = false,
  children,
  ...props
}: BadgeProps) {
  const base =
    'inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium tracking-wide transition-colors border'

  const variants = {
    default: 'bg-zinc-800/80 text-zinc-300 border-zinc-700/60',
    success: 'bg-emerald-950/60 text-emerald-300 border-emerald-800/60 shadow-xs shadow-emerald-900/30',
    warning: 'bg-amber-950/60 text-amber-300 border-amber-800/60 shadow-xs shadow-amber-900/30',
    destructive: 'bg-rose-950/60 text-rose-300 border-rose-800/60 shadow-xs shadow-rose-900/30',
    info: 'bg-sky-950/60 text-sky-300 border-sky-800/60 shadow-xs shadow-sky-900/30',
    purple: 'bg-purple-950/60 text-purple-300 border-purple-800/60 shadow-xs shadow-purple-900/30',
    outline: 'bg-transparent text-zinc-400 border-zinc-700',
  }

  const dotColors = {
    default: 'bg-zinc-400',
    success: 'bg-emerald-400',
    warning: 'bg-amber-400',
    destructive: 'bg-rose-400',
    info: 'bg-sky-400',
    purple: 'bg-purple-400',
    outline: 'bg-zinc-400',
  }

  return (
    <div className={cn(base, variants[variant], className)} {...props}>
      {dot && (
        <span
          className={cn(
            'h-1.5 w-1.5 rounded-full',
            dotColors[variant],
            pulse && 'animate-ping inline-block'
          )}
        />
      )}
      {children}
    </div>
  )
}

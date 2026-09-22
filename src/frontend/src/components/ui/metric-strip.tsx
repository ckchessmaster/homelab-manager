import React from 'react'
import { Badge } from './badge'

export interface MetricItem {
  id: string
  label: string
  value: string | number
  icon?: React.ComponentType<{ className?: string }>
  iconColor?: string
  subtext?: string
  badge?: string | number
  badgeVariant?: 'default' | 'success' | 'warning' | 'destructive' | 'info' | 'purple'
  badgeDot?: boolean
  onClick?: () => void
  className?: string
}

export interface MetricStripProps {
  items: MetricItem[]
  className?: string
}

export function MetricStrip({ items, className = '' }: MetricStripProps) {
  return (
    <div
      className={`h-11 min-h-[44px] flex items-center bg-zinc-900/60 border border-zinc-800/80 rounded-xl px-2 backdrop-blur-md overflow-x-auto divide-x divide-zinc-800/80 shadow-xs ${className}`}
    >
      {items.map((item) => {
        const Icon = item.icon
        const isClickable = Boolean(item.onClick)

        return (
          <div
            key={item.id}
            onClick={item.onClick}
            title={item.subtext}
            className={`flex items-center gap-2.5 px-3 py-1 shrink-0 transition-colors ${
              isClickable
                ? 'cursor-pointer hover:bg-zinc-800/50 rounded-lg group'
                : ''
            } ${item.className || ''}`}
          >
            {Icon && (
              <Icon
                className={`w-4 h-4 shrink-0 ${
                  item.iconColor || 'text-zinc-400'
                } ${isClickable ? 'group-hover:scale-105 transition-transform' : ''}`}
              />
            )}

            <div className="flex items-center gap-2 text-xs">
              <span className="font-medium text-zinc-400 whitespace-nowrap">
                {item.label}:
              </span>
              <span className="font-bold text-zinc-100 font-mono text-sm leading-none">
                {item.value}
              </span>
            </div>

            {item.badge !== undefined && item.badge !== null && (
              <Badge
                variant={item.badgeVariant || 'default'}
                dot={item.badgeDot}
                className="text-[10px] px-1.5 py-0 h-4.5 font-mono ml-0.5"
              >
                {item.badge}
              </Badge>
            )}
          </div>
        )
      })}
    </div>
  )
}

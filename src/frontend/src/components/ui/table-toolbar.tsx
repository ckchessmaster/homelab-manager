import React from 'react'
import { Search, X } from 'lucide-react'
import { Input } from './input'

interface TableToolbarProps {
  children?: React.ReactNode
  className?: string
}

export function TableToolbar({ children, className = '' }: TableToolbarProps) {
  return (
    <div
      className={`min-h-12 p-2 sm:p-2.5 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md flex flex-col md:flex-row items-stretch md:items-center justify-between gap-2.5 shadow-xs ${className}`}
    >
      {children}
    </div>
  )
}

interface TableToolbarSearchProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
}

export function TableToolbarSearch({
  value,
  onChange,
  placeholder = 'Search...',
  className = '',
}: TableToolbarSearchProps) {
  return (
    <div className={`relative w-full md:w-auto flex-1 max-w-full md:max-w-sm ${className}`}>
      <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500 pointer-events-none" />
      <Input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="h-9 pl-8.5 pr-8 bg-zinc-950/80 border-zinc-800 text-xs text-zinc-100 placeholder:text-zinc-500 rounded-lg focus:border-sky-500/80 focus:ring-1 focus:ring-sky-500/30 transition-all w-full"
      />
      {value && (
        <button
          type="button"
          onClick={() => onChange('')}
          className="absolute right-1 top-1/2 -translate-y-1/2 p-1.5 text-zinc-500 hover:text-zinc-300 rounded cursor-pointer transition-colors min-w-[32px] min-h-[32px] flex items-center justify-center"
          title="Clear search"
          aria-label="Clear search"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      )}
    </div>
  )
}

interface TableToolbarGroupProps {
  children: React.ReactNode
  className?: string
}

export function TableToolbarGroup({
  children,
  className = '',
}: TableToolbarGroupProps) {
  return (
    <div className={`flex flex-wrap items-center gap-2 ${className}`}>
      {children}
    </div>
  )
}

interface TableToolbarActionsProps {
  children: React.ReactNode
  className?: string
}

export function TableToolbarActions({
  children,
  className = '',
}: TableToolbarActionsProps) {
  return (
    <div className={`flex items-center gap-2 shrink-0 ${className}`}>
      {children}
    </div>
  )
}

interface TableToolbarSegmentProps<T extends string> {
  options: { id: T; label: string; icon?: React.ComponentType<{ className?: string }> }[]
  value: T
  onChange: (value: T) => void
  className?: string
}

export function TableToolbarSegment<T extends string>({
  options,
  value,
  onChange,
  className = '',
}: TableToolbarSegmentProps<T>) {
  return (
    <div
      className={`h-9 flex items-center p-0.5 bg-zinc-950/80 border border-zinc-800 rounded-lg text-xs overflow-x-auto scrollbar-none max-w-full ${className}`}
    >
      {options.map((opt) => {
        const Icon = opt.icon
        const isSelected = value === opt.id

        return (
          <button
            key={opt.id}
            type="button"
            onClick={() => onChange(opt.id)}
            className={`h-7.5 px-2.5 rounded-md font-medium flex items-center gap-1.5 transition-all cursor-pointer whitespace-nowrap shrink-0 min-h-[30px] ${
              isSelected
                ? 'bg-zinc-800 text-zinc-100 shadow-xs border border-zinc-700/60'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
            }`}
          >
            {Icon && <Icon className="w-3.5 h-3.5" />}
            <span>{opt.label}</span>
          </button>
        )
      })}
    </div>
  )
}

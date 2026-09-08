import React, { useState, useRef, useEffect } from 'react'
import { useAuthUser } from './useAuthUser'
import { getApiKey } from '../../api/client'
import {
  Key,
  LogOut,
  Copy,
  Check,
  Settings,
  ChevronDown,
  ArrowRight,
} from 'lucide-react'

interface ApiKeyHeaderMenuProps {
  onOpenSettings: () => void
}

export const ApiKeyHeaderMenu: React.FC<ApiKeyHeaderMenuProps> = ({ onOpenSettings }) => {
  const { logout, switchAuthMode } = useAuthUser()
  const [isOpen, setIsOpen] = useState(false)
  const [copied, setCopied] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  const activeKey = getApiKey()
  const maskedKey = activeKey
    ? `${activeKey.slice(0, 4)}••••••••${activeKey.slice(-4)}`
    : '••••••••••••'

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }
    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [isOpen])

  const handleCopyKey = () => {
    if (!activeKey) return
    navigator.clipboard.writeText(activeKey)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  const handleDisconnect = async () => {
    setIsOpen(false)
    await logout()
  }

  return (
    <div className="relative" ref={menuRef}>
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 p-1.5 pr-2.5 rounded-lg bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-xs text-zinc-200 transition-all cursor-pointer shadow-sm hover:border-zinc-700"
        aria-haspopup="true"
        aria-expanded={isOpen}
      >
        <div className="h-6 w-6 rounded-md bg-emerald-950/80 border border-emerald-800/80 text-emerald-400 flex items-center justify-center">
          <Key className="h-3.5 w-3.5" />
        </div>
        <div className="flex flex-col items-start leading-none hidden sm:flex">
          <span className="text-[11px] font-medium text-zinc-200">API Key</span>
          <span className="text-[9px] font-mono text-emerald-400 font-semibold">Full Admin</span>
        </div>
        <ChevronDown className={`h-3.5 w-3.5 text-zinc-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div className="absolute right-0 mt-2 w-64 rounded-xl bg-zinc-900 border border-zinc-800 shadow-2xl z-50 p-2 text-xs animate-in fade-in zoom-in-95 duration-100">
          <div className="p-2 border-b border-zinc-800/80 space-y-1">
            <div className="flex items-center justify-between">
              <span className="font-semibold text-zinc-100">API Key Mode</span>
              <span className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-emerald-950 text-emerald-300 border border-emerald-800/60">
                Full Access
              </span>
            </div>
            <div className="text-[11px] font-mono text-zinc-400 truncate">
              {maskedKey}
            </div>
          </div>

          <div className="py-1 space-y-0.5">
            <button
              onClick={handleCopyKey}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800/60 text-left transition-colors cursor-pointer"
            >
              {copied ? <Check className="h-3.5 w-3.5 text-emerald-400" /> : <Copy className="h-3.5 w-3.5 text-zinc-400" />}
              <span>{copied ? 'Copied Key!' : 'Copy API Key'}</span>
            </button>

            <button
              onClick={() => {
                setIsOpen(false)
                onOpenSettings()
              }}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800/60 text-left transition-colors cursor-pointer"
            >
              <Settings className="h-3.5 w-3.5 text-zinc-400" />
              <span>Security & Key Settings</span>
            </button>

            <button
              onClick={() => {
                setIsOpen(false)
                switchAuthMode?.('oidc')
              }}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800/60 text-left transition-colors cursor-pointer"
            >
              <ArrowRight className="h-3.5 w-3.5 text-sky-400" />
              <span>Switch to OIDC (Zitadel)</span>
            </button>
          </div>

          <div className="pt-1 border-t border-zinc-800/80">
            <button
              onClick={handleDisconnect}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg text-red-400 hover:text-red-300 hover:bg-red-950/30 text-left transition-colors cursor-pointer"
            >
              <LogOut className="h-3.5 w-3.5" />
              <span>Disconnect / Lock</span>
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

import { useState } from 'react'
import {
  Activity,
  Plus,
  Trash2,
  Globe,
  Sparkles,
} from 'lucide-react'

export interface HealthProbesSectionProps {
  probeUrls: string[]
  onAddProbe: (url: string) => void
  onRemoveProbe: (index: number) => void
  hostIp?: string
}

export function HealthProbesSection({
  probeUrls,
  onAddProbe,
  onRemoveProbe,
  hostIp = '127.0.0.1',
}: HealthProbesSectionProps) {
  const [newUrl, setNewUrl] = useState('')

  const handleAdd = () => {
    if (!newUrl.trim()) return
    onAddProbe(newUrl.trim())
    setNewUrl('')
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault()
      handleAdd()
    }
  }

  const presets = [
    { label: 'HTTP Web (:80)', url: `http://${hostIp}:80` },
    { label: 'HTTPS Web (:443)', url: `https://${hostIp}:443` },
    { label: 'SSH (:22)', url: `tcp://${hostIp}:22` },
    { label: 'Proxmox (:8006)', url: `https://${hostIp}:8006` },
    { label: 'Kubelet (:10250)', url: `https://${hostIp}:10250/healthz` },
  ]

  return (
    <div className="p-4 rounded-xl bg-zinc-950/80 border border-zinc-800 space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
            <Activity className="w-4 h-4" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold text-zinc-100">
                Post-Upgrade Synthetic Health Probes
              </span>
              <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-400">
                {probeUrls.length} configured
              </span>
            </div>
            <p className="text-[11px] text-zinc-400 mt-0.5 leading-relaxed">
              Synthetic HTTP and TCP endpoint verification executed before marking the upgrade complete.
            </p>
          </div>
        </div>
      </div>

      {/* Quick Add Presets */}
      <div className="space-y-1.5 pt-1">
        <div className="text-[10px] uppercase font-mono tracking-wider text-zinc-500 flex items-center gap-1">
          <Sparkles className="w-3 h-3 text-emerald-400" />
          Quick-Add Presets
        </div>
        <div className="flex items-center gap-1.5 flex-wrap">
          {presets.map((preset) => {
            const isAlreadyAdded = probeUrls.includes(preset.url)
            return (
              <button
                key={preset.label}
                type="button"
                onClick={() => !isAlreadyAdded && onAddProbe(preset.url)}
                disabled={isAlreadyAdded}
                className={`px-2 py-1 rounded text-[11px] font-mono transition-colors ${
                  isAlreadyAdded
                    ? 'bg-zinc-900/60 text-zinc-600 border border-zinc-800/60 cursor-not-allowed'
                    : 'bg-zinc-900 hover:bg-zinc-800 text-zinc-300 border border-zinc-700 hover:border-zinc-600 cursor-pointer'
                }`}
              >
                + {preset.label}
              </button>
            )
          })}
        </div>
      </div>

      {/* Active Probes List */}
      {probeUrls.length > 0 && (
        <div className="space-y-1.5 pt-1">
          {probeUrls.map((url, idx) => (
            <div
              key={idx}
              className="flex items-center justify-between p-2 rounded-lg bg-zinc-900/60 border border-zinc-800/80 text-xs font-mono text-zinc-200"
            >
              <span className="flex items-center gap-2 truncate mr-2">
                <Globe className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                <span className="truncate">{url}</span>
              </span>
              <button
                type="button"
                onClick={() => onRemoveProbe(idx)}
                className="p-1 rounded text-zinc-500 hover:text-rose-400 hover:bg-rose-950/30 transition-colors shrink-0"
                title="Remove probe"
              >
                <Trash2 className="w-3.5 h-3.5" />
              </button>
            </div>
          ))}
        </div>
      )}

      {/* Custom Probe Input */}
      <div className="flex items-center gap-2 pt-1">
        <input
          type="text"
          value={newUrl}
          onChange={(e) => setNewUrl(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Custom URL (e.g. http://192.168.1.50:8080/health)"
          className="flex-1 px-3 py-1.5 text-xs bg-zinc-900 border border-zinc-700/80 rounded-lg text-zinc-200 focus:outline-none focus:ring-1 focus:ring-emerald-500 font-mono"
        />
        <button
          type="button"
          onClick={handleAdd}
          disabled={!newUrl.trim()}
          className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg bg-zinc-800 hover:bg-zinc-700 disabled:opacity-40 text-xs text-zinc-200 font-medium transition-colors border border-zinc-700 cursor-pointer"
        >
          <Plus className="w-3.5 h-3.5" />
          Add
        </button>
      </div>
    </div>
  )
}

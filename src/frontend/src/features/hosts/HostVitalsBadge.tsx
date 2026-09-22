import type { HostVitals } from '../../api/hosts'
import { Activity, Cpu, HardDrive, Zap, Thermometer, Clock } from 'lucide-react'

interface HostVitalsBadgeProps {
  vitals?: HostVitals | null
  mode?: 'compact' | 'detailed'
  className?: string
}

function getUsageColor(pct?: number | null): string {
  if (pct == null) return 'text-zinc-400'
  if (pct >= 90) return 'text-rose-400'
  if (pct >= 75) return 'text-amber-400'
  return 'text-emerald-400'
}

function getBarColor(pct?: number | null): string {
  if (pct == null) return 'bg-zinc-600'
  if (pct >= 90) return 'bg-rose-500'
  if (pct >= 75) return 'bg-amber-500'
  return 'bg-emerald-500'
}

function formatUptime(seconds?: number | null): string {
  if (!seconds || seconds <= 0) return 'N/A'
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${mins}m`
  return `${mins}m`
}

export function HostVitalsBadge({
  vitals,
  mode = 'compact',
  className = '',
}: HostVitalsBadgeProps) {
  if (!vitals) return null

  const hasCpu = vitals.cpuUsagePct != null
  const hasMem = vitals.memoryUsagePct != null
  const hasDisk = vitals.diskFreePct != null
  const hasPower = vitals.powerWatts != null
  const hasTemp = vitals.temperatureCelsius != null

  // If no metric data is present, render nothing to keep the view clean
  if (!hasCpu && !hasMem && !hasDisk && !hasPower && !hasTemp) {
    return null
  }

  if (mode === 'compact') {
    const diskUsedPct = hasDisk ? Math.max(0, Math.min(100, 100 - (vitals.diskFreePct ?? 0))) : null

    const tooltipParts: string[] = []
    if (hasCpu) tooltipParts.push(`CPU: ${vitals.cpuUsagePct?.toFixed(1)}%`)
    if (hasMem) tooltipParts.push(`RAM: ${vitals.memoryUsagePct?.toFixed(1)}%`)
    if (hasDisk) tooltipParts.push(`Disk Free: ${vitals.diskFreePct?.toFixed(1)}%`)
    if (hasPower) tooltipParts.push(`Power: ${vitals.powerWatts?.toFixed(0)}W`)
    if (hasTemp) tooltipParts.push(`Temp: ${vitals.temperatureCelsius?.toFixed(1)}°C`)

    return (
      <div
        className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-zinc-900/90 border border-zinc-800 text-[11px] font-mono select-none ${className}`}
        title={tooltipParts.join(' | ')}
      >
        {hasCpu && (
          <span className="flex items-center gap-1">
            <span className="text-zinc-500 font-sans text-[10px]">CPU</span>
            <span className={`font-semibold ${getUsageColor(vitals.cpuUsagePct)}`}>
              {vitals.cpuUsagePct?.toFixed(0)}%
            </span>
          </span>
        )}

        {hasCpu && hasMem && <span className="text-zinc-700">·</span>}

        {hasMem && (
          <span className="flex items-center gap-1">
            <span className="text-zinc-500 font-sans text-[10px]">RAM</span>
            <span className={`font-semibold ${getUsageColor(vitals.memoryUsagePct)}`}>
              {vitals.memoryUsagePct?.toFixed(0)}%
            </span>
          </span>
        )}

        {diskUsedPct != null && diskUsedPct >= 85 && (
          <>
            <span className="text-zinc-700">·</span>
            <span className="flex items-center gap-1" title={`Disk ${diskUsedPct.toFixed(0)}% used`}>
              <span className="text-zinc-500 font-sans text-[10px]">Disk</span>
              <span className={`font-semibold ${getUsageColor(diskUsedPct)}`}>
                {diskUsedPct.toFixed(0)}%
              </span>
            </span>
          </>
        )}

        {hasTemp && (
          <>
            <span className="text-zinc-700">·</span>
            <span className="text-zinc-400 font-mono text-[10px]" title={`Chassis Temp: ${vitals.temperatureCelsius?.toFixed(1)}°C`}>
              {vitals.temperatureCelsius?.toFixed(0)}°C
            </span>
          </>
        )}
      </div>
    )
  }

  // Detailed view for Inspector Drawer (<HostDetailsModal />)
  const diskUsedPct = hasDisk ? Math.max(0, Math.min(100, 100 - (vitals.diskFreePct ?? 0))) : null

  return (
    <div className={`p-3.5 bg-zinc-950/60 border border-zinc-800 rounded-xl space-y-3 ${className}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Activity className="h-4 w-4 text-sky-400" />
          <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider">
            Live Resource Vitals
          </h4>
        </div>
        {vitals.source && (
          <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-400 border border-zinc-800">
            Source: {vitals.source}
          </span>
        )}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        {/* CPU */}
        {hasCpu ? (
          <div className="p-2.5 bg-zinc-900/50 rounded-lg border border-zinc-800/80 space-y-1.5">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400 flex items-center gap-1">
                <Cpu className="h-3 w-3 text-sky-400" />
                CPU
              </span>
              <span className={`font-mono font-bold ${getUsageColor(vitals.cpuUsagePct)}`}>
                {vitals.cpuUsagePct?.toFixed(1)}%
              </span>
            </div>
            <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
              <div
                className={`h-full transition-all duration-500 rounded-full ${getBarColor(vitals.cpuUsagePct)}`}
                style={{ width: `${Math.min(100, Math.max(2, vitals.cpuUsagePct ?? 0))}%` }}
              />
            </div>
          </div>
        ) : (
          <div className="p-2.5 bg-zinc-900/30 rounded-lg border border-dashed border-zinc-800 text-xs text-zinc-500">
            CPU: N/A
          </div>
        )}

        {/* RAM */}
        {hasMem ? (
          <div className="p-2.5 bg-zinc-900/50 rounded-lg border border-zinc-800/80 space-y-1.5">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400 flex items-center gap-1">
                <Activity className="h-3 w-3 text-purple-400" />
                Memory
              </span>
              <span className={`font-mono font-bold ${getUsageColor(vitals.memoryUsagePct)}`}>
                {vitals.memoryUsagePct?.toFixed(1)}%
              </span>
            </div>
            <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
              <div
                className={`h-full transition-all duration-500 rounded-full ${getBarColor(vitals.memoryUsagePct)}`}
                style={{ width: `${Math.min(100, Math.max(2, vitals.memoryUsagePct ?? 0))}%` }}
              />
            </div>
          </div>
        ) : (
          <div className="p-2.5 bg-zinc-900/30 rounded-lg border border-dashed border-zinc-800 text-xs text-zinc-500">
            Memory: N/A
          </div>
        )}

        {/* Disk */}
        {hasDisk ? (
          <div className="p-2.5 bg-zinc-900/50 rounded-lg border border-zinc-800/80 space-y-1.5">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400 flex items-center gap-1">
                <HardDrive className="h-3 w-3 text-emerald-400" />
                Disk
              </span>
              <span className={`font-mono font-bold ${getUsageColor(diskUsedPct)}`}>
                {vitals.diskFreePct?.toFixed(1)}% free
              </span>
            </div>
            <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
              <div
                className={`h-full transition-all duration-500 rounded-full ${getBarColor(diskUsedPct)}`}
                style={{ width: `${Math.min(100, Math.max(2, diskUsedPct ?? 0))}%` }}
              />
            </div>
          </div>
        ) : (
          <div className="p-2.5 bg-zinc-900/30 rounded-lg border border-dashed border-zinc-800 text-xs text-zinc-500">
            Disk: N/A
          </div>
        )}
      </div>

      {/* Secondary Hardware Vitals (Power, Temperature, Uptime) if present */}
      {(hasPower || hasTemp || vitals.uptimeSeconds != null) && (
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-2 pt-2 border-t border-zinc-800/60 text-xs">
          {hasPower && (
            <div className="flex items-center gap-1.5 text-zinc-300">
              <Zap className="h-3.5 w-3.5 text-amber-400 shrink-0" />
              <span className="text-zinc-500 text-[11px]">Power:</span>
              <span className="font-mono font-semibold">{vitals.powerWatts?.toFixed(0)} W</span>
            </div>
          )}
          {hasTemp && (
            <div className="flex items-center gap-1.5 text-zinc-300">
              <Thermometer className="h-3.5 w-3.5 text-rose-400 shrink-0" />
              <span className="text-zinc-500 text-[11px]">Thermal:</span>
              <span className="font-mono font-semibold">{vitals.temperatureCelsius?.toFixed(1)} °C</span>
            </div>
          )}
          {vitals.uptimeSeconds != null && (
            <div className="flex items-center gap-1.5 text-zinc-300">
              <Clock className="h-3.5 w-3.5 text-sky-400 shrink-0" />
              <span className="text-zinc-500 text-[11px]">Uptime:</span>
              <span className="font-mono">{formatUptime(vitals.uptimeSeconds)}</span>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

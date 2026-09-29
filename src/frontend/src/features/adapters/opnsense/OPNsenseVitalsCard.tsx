import { Cpu, HardDrive, Clock, Thermometer, Database } from 'lucide-react'
import { useOPNsenseVitals } from './useOPNsense'

interface OPNsenseVitalsCardProps {
  instanceId: string
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

export function OPNsenseVitalsCard({ instanceId }: OPNsenseVitalsCardProps) {
  const { data: vitals, isLoading } = useOPNsenseVitals(instanceId)

  if (isLoading || !vitals) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 animate-pulse">
        {[1, 2, 3, 4].map((n) => (
          <div key={n} className="h-24 bg-zinc-900/40 border border-zinc-800 rounded-xl" />
        ))}
      </div>
    )
  }

  const memUsedStr = formatBytes(vitals.memoryUsedBytes)
  const memTotalStr = formatBytes(vitals.memoryTotalBytes)
  const diskUsedStr = formatBytes(vitals.diskUsedBytes)
  const diskTotalStr = formatBytes(vitals.diskTotalBytes)

  return (
    <div className="space-y-3">
      <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center justify-between">
        <span>Hardware Vitals & System Resources</span>
        {vitals.lastConfigChange && (
          <span className="text-[11px] text-zinc-500 font-normal font-mono lowercase">
            cfg: {vitals.lastConfigChange}
          </span>
        )}
      </h4>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        {/* CPU Load */}
        <div className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <div className="flex items-center justify-between text-zinc-400">
            <span className="text-xs font-medium flex items-center gap-1.5">
              <Cpu className="h-3.5 w-3.5 text-sky-400" />
              Load Average
            </span>
            <span className="text-[11px] font-mono text-zinc-300">
              {vitals.cpuLoadAverage?.[0] ? vitals.cpuLoadAverage[0].toFixed(2) : '0.00'}
            </span>
          </div>

          <div className="flex items-center justify-between text-xs font-mono pt-1 text-zinc-300">
            <div>
              <span className="text-[10px] text-zinc-500 block font-sans">1 min</span>
              <span className="text-sky-400 font-semibold">{vitals.cpuLoadAverage?.[0]?.toFixed(2) ?? '0.00'}</span>
            </div>
            <div>
              <span className="text-[10px] text-zinc-500 block font-sans">5 min</span>
              <span>{vitals.cpuLoadAverage?.[1]?.toFixed(2) ?? '0.00'}</span>
            </div>
            <div>
              <span className="text-[10px] text-zinc-500 block font-sans">15 min</span>
              <span>{vitals.cpuLoadAverage?.[2]?.toFixed(2) ?? '0.00'}</span>
            </div>
          </div>
        </div>

        {/* Memory Gauge */}
        <div className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <div className="flex items-center justify-between text-zinc-400">
            <span className="text-xs font-medium flex items-center gap-1.5">
              <Database className="h-3.5 w-3.5 text-emerald-400" />
              Memory
            </span>
            <span className="text-[11px] font-mono font-bold text-zinc-200">
              {vitals.memoryUsagePercent}%
            </span>
          </div>

          <div className="w-full bg-zinc-900 rounded-full h-2 overflow-hidden border border-zinc-800">
            <div
              className={`h-full rounded-full transition-all ${
                vitals.memoryUsagePercent > 85
                  ? 'bg-red-500'
                  : vitals.memoryUsagePercent > 70
                  ? 'bg-amber-500'
                  : 'bg-emerald-500'
              }`}
              style={{ width: `${Math.min(100, vitals.memoryUsagePercent)}%` }}
            />
          </div>

          <div className="flex items-center justify-between text-[11px] font-mono text-zinc-400 pt-0.5">
            <span>Used: <strong className="text-zinc-200">{memUsedStr}</strong></span>
            <span>Total: {memTotalStr}</span>
          </div>
        </div>

        {/* Disk Gauge */}
        <div className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <div className="flex items-center justify-between text-zinc-400">
            <span className="text-xs font-medium flex items-center gap-1.5">
              <HardDrive className="h-3.5 w-3.5 text-purple-400" />
              Storage Pool
            </span>
            <span className="text-[11px] font-mono font-bold text-zinc-200">
              {vitals.diskUsagePercent}%
            </span>
          </div>

          <div className="w-full bg-zinc-900 rounded-full h-2 overflow-hidden border border-zinc-800">
            <div
              className={`h-full rounded-full transition-all ${
                vitals.diskUsagePercent > 85 ? 'bg-red-500' : 'bg-purple-500'
              }`}
              style={{ width: `${Math.min(100, vitals.diskUsagePercent)}%` }}
            />
          </div>

          <div className="flex items-center justify-between text-[11px] font-mono text-zinc-400 pt-0.5">
            <span>Used: <strong className="text-zinc-200">{diskUsedStr}</strong></span>
            <span>Total: {diskTotalStr}</span>
          </div>
        </div>

        {/* Uptime & Temps */}
        <div className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <div className="flex items-center justify-between text-zinc-400">
            <span className="text-xs font-medium flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 text-amber-400" />
              System Uptime
            </span>
            {vitals.temperatures && Object.values(vitals.temperatures)[0] && (
              <span className="text-[11px] font-mono text-emerald-400 flex items-center gap-0.5">
                <Thermometer className="h-3 w-3" />
                {Object.values(vitals.temperatures)[0]}°C
              </span>
            )}
          </div>

          <p className="text-xs font-mono font-bold text-zinc-100 pt-1">
            {vitals.uptimeFormatted}
          </p>

          <p className="text-[11px] text-zinc-500 truncate">
            {vitals.temperatures
              ? Object.entries(vitals.temperatures)
                  .map(([name, temp]) => `${name}: ${temp}°C`)
                  .join(' | ')
              : 'Thermal sensors normal'}
          </p>
        </div>
      </div>
    </div>
  )
}

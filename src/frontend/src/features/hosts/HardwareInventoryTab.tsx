import { useState } from 'react'
import {
  HardDrive,
  Cpu,
  Zap,
  Activity,
  RefreshCw,
  AlertTriangle,
  AlertCircle,
  CheckCircle,
  Shield,
  Layers,
  Thermometer,
} from 'lucide-react'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { useHostHardware, useTriggerHardwareScan } from './useHosts'
import type {
  PhysicalDisk,
  StorageController,
  PowerSupply,
  MemoryModule,
  ZfsPoolHealth,
  HardwareHealthStatus,
} from '../../api/hosts'

interface HardwareInventoryTabProps {
  hostId: string
  isOnline?: boolean
}

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  const formatted = (bytes / Math.pow(1024, i)).toFixed(i >= 3 ? 1 : 0)
  return `${formatted} ${units[i]}`
}

function getHealthBadge(status: HardwareHealthStatus) {
  switch (status) {
    case 'Ok':
      return (
        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-emerald-400 bg-emerald-950/40 px-2 py-0.5 rounded-full border border-emerald-800/40">
          <CheckCircle className="h-3 w-3 text-emerald-400" />
          Healthy
        </span>
      )
    case 'Warning':
      return (
        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-amber-300 bg-amber-950/60 px-2 py-0.5 rounded-full border border-amber-600/50">
          <AlertTriangle className="h-3 w-3 text-amber-400" />
          Warning
        </span>
      )
    case 'Critical':
      return (
        <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-rose-300 bg-rose-950/80 px-2 py-0.5 rounded-full border border-rose-600/60 shadow-xs shadow-rose-900/40">
          <AlertCircle className="h-3 w-3 text-rose-400" />
          Critical
        </span>
      )
    default:
      return (
        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-zinc-400 bg-zinc-800/40 px-2 py-0.5 rounded-full border border-zinc-700/40">
          Unknown
        </span>
      )
  }
}

function getMediaBadge(mediaType: string) {
  const type = mediaType?.toUpperCase() || 'UNKNOWN'
  if (type.includes('NVME')) {
    return (
      <Badge variant="purple" className="text-[10px] px-1.5 py-0 font-mono">
        NVMe
      </Badge>
    )
  }
  if (type.includes('SSD')) {
    return (
      <Badge variant="info" className="text-[10px] px-1.5 py-0 font-mono">
        SSD
      </Badge>
    )
  }
  if (type.includes('HDD')) {
    return (
      <Badge variant="outline" className="text-[10px] px-1.5 py-0 font-mono text-zinc-400">
        HDD
      </Badge>
    )
  }
  return (
    <Badge variant="outline" className="text-[10px] px-1.5 py-0 font-mono text-zinc-500">
      {mediaType}
    </Badge>
  )
}

function DiskCard({ disk }: { disk: PhysicalDisk }) {
  const wear = disk.wearOutPercentage
  const temp = disk.temperatureCelsius

  let wearColor = 'bg-emerald-500'
  let wearText = 'text-emerald-400'
  if (wear !== null && wear !== undefined) {
    if (wear <= 5) {
      wearColor = 'bg-rose-500'
      wearText = 'text-rose-400'
    } else if (wear <= 20) {
      wearColor = 'bg-amber-500'
      wearText = 'text-amber-400'
    }
  }

  let tempColor = 'text-zinc-400'
  if (temp !== null && temp !== undefined) {
    if (temp >= 65) tempColor = 'text-rose-400 font-semibold'
    else if (temp >= 50) tempColor = 'text-amber-400'
    else tempColor = 'text-emerald-400'
  }

  const isSmartPass =
    disk.smartHealthStatus?.toUpperCase() === 'PASSED' ||
    disk.smartHealthStatus?.toUpperCase() === 'OK'

  return (
    <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2 hover:border-zinc-700/80 transition-colors">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2">
          <HardDrive className="h-4 w-4 text-sky-400 shrink-0" />
          <span className="font-mono text-xs font-semibold text-zinc-100">
            {disk.name || disk.deviceId}
          </span>
          {getMediaBadge(disk.mediaType)}
          {disk.slotLocation && (
            <span className="text-[11px] font-mono px-1.5 py-0.2 rounded bg-zinc-800 text-zinc-300 border border-zinc-700/50">
              {disk.slotLocation}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {disk.smartHealthStatus && (
            <span
              className={`text-[10px] font-mono px-1.5 py-0.5 rounded font-medium border ${
                isSmartPass
                  ? 'bg-emerald-950/40 text-emerald-400 border-emerald-800/40'
                  : 'bg-rose-950/60 text-rose-300 border-rose-700/60'
              }`}
            >
              SMART: {disk.smartHealthStatus}
            </span>
          )}
          {getHealthBadge(disk.status)}
        </div>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs text-zinc-400 pt-1 border-t border-zinc-800/40">
        <div>
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">Model</span>
          <span className="text-zinc-300 font-medium truncate block" title={disk.model || 'Unknown'}>
            {disk.model || 'Unknown'}
          </span>
        </div>
        <div>
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">Capacity</span>
          <span className="font-mono text-zinc-300 font-medium block">
            {formatBytes(disk.sizeBytes)}
          </span>
        </div>
        <div>
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">Temperature</span>
          <span className={`font-mono text-xs flex items-center gap-1 ${tempColor}`}>
            <Thermometer className="h-3 w-3 inline" />
            {temp !== null && temp !== undefined ? `${temp.toFixed(0)}°C` : 'N/A'}
          </span>
        </div>
        <div>
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider block">Serial</span>
          <span className="font-mono text-[11px] text-zinc-400 truncate block" title={disk.serialNumber || 'N/A'}>
            {disk.serialNumber || 'N/A'}
          </span>
        </div>
      </div>

      {wear !== null && wear !== undefined && (
        <div className="pt-1.5 space-y-1">
          <div className="flex items-center justify-between text-[11px]">
            <span className="text-zinc-500">SSD Endurance</span>
            <span className={`font-mono font-medium ${wearText}`}>
              {wear.toFixed(1)}% remaining life
            </span>
          </div>
          <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-500 ${wearColor}`}
              style={{ width: `${Math.max(0, Math.min(100, wear))}%` }}
            />
          </div>
        </div>
      )}
    </div>
  )
}

function ControllerCard({ controller }: { controller: StorageController }) {
  return (
    <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Activity className="h-4 w-4 text-purple-400 shrink-0" />
          <span className="text-xs font-semibold text-zinc-200">{controller.name}</span>
          {controller.model && (
            <span className="text-[11px] text-zinc-400 font-mono">({controller.model})</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {controller.batteryBackupHealthy !== null && controller.batteryBackupHealthy !== undefined && (
            <span
              className={`text-[10px] font-mono px-1.5 py-0.5 rounded border ${
                controller.batteryBackupHealthy
                  ? 'bg-emerald-950/40 text-emerald-400 border-emerald-800/40'
                  : 'bg-amber-950/60 text-amber-300 border-amber-700/60'
              }`}
            >
              BBU: {controller.batteryBackupHealthy ? 'Healthy' : 'Degraded'}
            </span>
          )}
          {getHealthBadge(controller.status)}
        </div>
      </div>
      {controller.firmwareVersion && (
        <div className="text-[11px] text-zinc-500 font-mono">
          Firmware: {controller.firmwareVersion}
        </div>
      )}
    </div>
  )
}

function PowerSupplyCard({ psu }: { psu: PowerSupply }) {
  return (
    <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Zap className="h-4 w-4 text-amber-400 shrink-0" />
          <span className="text-xs font-semibold text-zinc-200">{psu.name || psu.id}</span>
        </div>
        <div className="flex items-center gap-2">
          <span
            className={`text-[10px] font-mono px-1.5 py-0.5 rounded border ${
              psu.redundancyHealthy
                ? 'bg-emerald-950/40 text-emerald-400 border-emerald-800/40'
                : 'bg-amber-950/60 text-amber-300 border-amber-700/60'
            }`}
          >
            {psu.redundancyHealthy ? 'Redundant' : 'Redundancy Lost'}
          </span>
          {getHealthBadge(psu.status)}
        </div>
      </div>
      <div className="flex items-center gap-4 text-xs font-mono text-zinc-400 pt-1">
        {psu.outputWatts !== null && psu.outputWatts !== undefined && (
          <div>
            <span className="text-zinc-500 text-[10px] block uppercase">Output</span>
            <span className="text-zinc-200">{psu.outputWatts.toFixed(0)} W</span>
          </div>
        )}
        {psu.inputWatts !== null && psu.inputWatts !== undefined && (
          <div>
            <span className="text-zinc-500 text-[10px] block uppercase">Input</span>
            <span className="text-zinc-200">{psu.inputWatts.toFixed(0)} W</span>
          </div>
        )}
        {psu.lineInputVoltage !== null && psu.lineInputVoltage !== undefined && (
          <div>
            <span className="text-zinc-500 text-[10px] block uppercase">Voltage</span>
            <span className="text-zinc-200">{psu.lineInputVoltage.toFixed(0)} V</span>
          </div>
        )}
      </div>
    </div>
  )
}

function MemoryModuleCard({ dimm }: { dimm: MemoryModule }) {
  const hasEccIssues =
    (dimm.uncorrectableEccErrors ?? 0) > 0 || (dimm.correctableEccErrors ?? 0) > 10

  return (
    <div className="p-2.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl flex items-center justify-between text-xs">
      <div className="flex items-center gap-2">
        <Cpu className="h-3.5 w-3.5 text-sky-400 shrink-0" />
        <span className="font-mono font-semibold text-zinc-200">{dimm.slotLocation}</span>
        <span className="text-zinc-400">({formatBytes(dimm.sizeBytes)})</span>
        {dimm.speedMhz && (
          <span className="text-[11px] font-mono text-zinc-500">{dimm.speedMhz} MHz</span>
        )}
      </div>
      <div className="flex items-center gap-2">
        {hasEccIssues ? (
          <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-rose-950/60 text-rose-300 border border-rose-700/60">
            ECC: {dimm.uncorrectableEccErrors ?? 0} uncorr / {dimm.correctableEccErrors ?? 0} corr
          </span>
        ) : (
          <span className="text-[10px] font-mono text-zinc-500">0 ECC Errors</span>
        )}
        {getHealthBadge(dimm.status)}
      </div>
    </div>
  )
}

function ZfsPoolCard({ pool }: { pool: ZfsPoolHealth }) {
  const isOnline = pool.state?.toUpperCase() === 'ONLINE'
  const isDegraded = pool.state?.toUpperCase() === 'DEGRADED'

  const usedPct =
    pool.sizeBytes > 0
      ? Math.round((pool.allocatedBytes / pool.sizeBytes) * 100)
      : 0

  return (
    <div className="p-3 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Layers className="h-4 w-4 text-purple-400 shrink-0" />
          <span className="font-mono text-xs font-semibold text-zinc-100">{pool.poolName}</span>
          <span className="text-[11px] text-zinc-500 font-mono">
            {formatBytes(pool.sizeBytes)} pool
          </span>
        </div>
        <span
          className={`text-[10px] font-mono px-2 py-0.5 rounded-full font-semibold border ${
            isOnline
              ? 'bg-emerald-950/40 text-emerald-400 border-emerald-800/40'
              : isDegraded
              ? 'bg-amber-950/60 text-amber-300 border-amber-600/60'
              : 'bg-rose-950/80 text-rose-300 border-rose-600/60'
          }`}
        >
          {pool.state}
        </span>
      </div>

      <div className="space-y-1">
        <div className="flex items-center justify-between text-[11px] text-zinc-400">
          <span>Allocated: {formatBytes(pool.allocatedBytes)} ({usedPct}%)</span>
          <span>Free: {formatBytes(pool.freeBytes)}</span>
        </div>
        <div className="h-1.5 w-full bg-zinc-800 rounded-full overflow-hidden">
          <div
            className={`h-full rounded-full ${
              usedPct >= 90 ? 'bg-rose-500' : usedPct >= 75 ? 'bg-amber-500' : 'bg-purple-500'
            }`}
            style={{ width: `${Math.min(100, usedPct)}%` }}
          />
        </div>
      </div>
    </div>
  )
}

export function HardwareInventoryTab({ hostId, isOnline = true }: HardwareInventoryTabProps) {
  const { data: hardware, isLoading, error } = useHostHardware(hostId)
  const scanMutation = useTriggerHardwareScan()
  const [scanMessage, setScanMessage] = useState<string | null>(null)

  const handleScanNow = async () => {
    setScanMessage(null)
    try {
      await scanMutation.mutateAsync(hostId)
      setScanMessage('Hardware telemetry scan successfully completed.')
      setTimeout(() => setScanMessage(null), 4000)
    } catch (err) {
      setScanMessage(err instanceof Error ? err.message : 'Scan failed')
    }
  }

  if (isLoading) {
    return (
      <div className="p-8 text-center border border-zinc-800 rounded-xl bg-zinc-950/40 space-y-2">
        <RefreshCw className="h-5 w-5 animate-spin mx-auto text-sky-400" />
        <p className="text-xs text-zinc-400">Loading hardware telemetry and SMART status...</p>
      </div>
    )
  }

  if (error || !hardware) {
    return (
      <div className="p-6 text-center border border-zinc-800 rounded-xl bg-zinc-950/40 space-y-3">
        <AlertTriangle className="h-6 w-6 text-amber-400 mx-auto" />
        <p className="text-xs text-zinc-300">
          No hardware telemetry recorded yet for this host.
        </p>
        <p className="text-[11px] text-zinc-500 max-w-sm mx-auto">
          Ensure the agent daemon is online or that BMC/Proxmox adapters are configured to ingest drive and component vitals.
        </p>
        <Button
          size="sm"
          variant="outline"
          onClick={handleScanNow}
          disabled={scanMutation.isPending || !isOnline}
          className="gap-1.5 text-xs"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${scanMutation.isPending ? 'animate-spin' : ''}`} />
          Trigger Scan Now
        </Button>
      </div>
    )
  }

  const hasDisks = hardware.disks && hardware.disks.length > 0
  const hasControllers = hardware.controllers && hardware.controllers.length > 0
  const hasPowerSupplies = hardware.powerSupplies && hardware.powerSupplies.length > 0
  const hasMemory = hardware.memoryModules && hardware.memoryModules.length > 0
  const hasZfs = hardware.zfsPools && hardware.zfsPools.length > 0

  return (
    <div className="space-y-4">
      {/* Top Controls & Status Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 bg-zinc-950/60 border border-zinc-800 rounded-xl">
        <div className="flex items-center gap-2.5">
          {getHealthBadge(hardware.overallHealth)}
          <span className="text-xs text-zinc-400">
            Source: <strong className="text-zinc-200 font-mono">{hardware.source || 'agent'}</strong>
          </span>
          <span className="text-zinc-600">•</span>
          <span className="text-[11px] text-zinc-500">
            Scanned {new Date(hardware.collectedAt).toLocaleTimeString()}
          </span>
        </div>

        <Button
          size="sm"
          variant="outline"
          onClick={handleScanNow}
          disabled={scanMutation.isPending}
          className="gap-1.5 text-xs shrink-0 self-start sm:self-auto border-sky-800/50 text-sky-300 hover:bg-sky-950/40 cursor-pointer"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${scanMutation.isPending ? 'animate-spin' : ''}`} />
          {scanMutation.isPending ? 'Scanning...' : 'Scan Now'}
        </Button>
      </div>

      {scanMessage && (
        <div className="p-2.5 bg-zinc-900 border border-sky-800/50 rounded-lg text-xs text-sky-300 animate-in fade-in flex items-center gap-2">
          <Activity className="h-3.5 w-3.5 text-sky-400 shrink-0" />
          <span>{scanMessage}</span>
        </div>
      )}

      {/* Health Alerts Callout */}
      {hardware.healthAlerts && hardware.healthAlerts.length > 0 && (
        <div className="p-3 bg-amber-950/30 border border-amber-800/60 rounded-xl space-y-1.5">
          <div className="flex items-center gap-2 text-xs font-semibold text-amber-300">
            <AlertTriangle className="h-4 w-4 text-amber-400 shrink-0" />
            <span>Hardware Alerts Detected ({hardware.healthAlerts.length})</span>
          </div>
          <ul className="list-disc list-inside text-xs text-amber-200/90 space-y-0.5">
            {hardware.healthAlerts.map((alert, idx) => (
              <li key={idx}>{alert}</li>
            ))}
          </ul>
        </div>
      )}

      {/* 1. Physical Disks */}
      <div className="space-y-2.5">
        <div className="flex items-center justify-between">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
            <HardDrive className="h-3.5 w-3.5 text-sky-400" />
            Physical Disks & SMART ({hardware.disks?.length || 0})
          </h4>
        </div>

        {hasDisks ? (
          <div className="space-y-2">
            {hardware.disks.map((disk) => (
              <DiskCard key={disk.deviceId} disk={disk} />
            ))}
          </div>
        ) : (
          <p className="text-xs text-zinc-500 italic p-3 border border-dashed border-zinc-800 rounded-xl">
            No physical disk telemetry collected.
          </p>
        )}
      </div>

      {/* 2. ZFS Pools */}
      {hasZfs && (
        <div className="space-y-2.5">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
            <Layers className="h-3.5 w-3.5 text-purple-400" />
            ZFS Storage Pools ({hardware.zfsPools?.length || 0})
          </h4>
          <div className="space-y-2">
            {hardware.zfsPools!.map((pool) => (
              <ZfsPoolCard key={pool.poolName} pool={pool} />
            ))}
          </div>
        </div>
      )}

      {/* 3. Storage Controllers */}
      {hasControllers && (
        <div className="space-y-2.5">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
            <Shield className="h-3.5 w-3.5 text-purple-400" />
            Storage Controllers & RAID ({hardware.controllers.length})
          </h4>
          <div className="space-y-2">
            {hardware.controllers.map((c) => (
              <ControllerCard key={c.id} controller={c} />
            ))}
          </div>
        </div>
      )}

      {/* 4. Power Supplies */}
      {hasPowerSupplies && (
        <div className="space-y-2.5">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
            <Zap className="h-3.5 w-3.5 text-amber-400" />
            Power Supplies ({hardware.powerSupplies.length})
          </h4>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {hardware.powerSupplies.map((psu) => (
              <PowerSupplyCard key={psu.id} psu={psu} />
            ))}
          </div>
        </div>
      )}

      {/* 5. Memory Modules */}
      {hasMemory && (
        <div className="space-y-2.5">
          <h4 className="text-xs font-semibold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5">
            <Cpu className="h-3.5 w-3.5 text-sky-400" />
            Memory Modules & ECC ({hardware.memoryModules.length})
          </h4>
          <div className="space-y-1.5">
            {hardware.memoryModules.map((dimm) => (
              <MemoryModuleCard key={dimm.slotLocation} dimm={dimm} />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

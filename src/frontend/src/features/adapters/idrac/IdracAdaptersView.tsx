import { useState } from 'react'
import {
  Cpu,
  Plus,
  Power,
  RotateCcw,
  RefreshCw,
  Trash2,
  Edit2,
  Thermometer,
  Fan,
  Zap,
  ShieldCheck,
  ExternalLink,
  PowerOff,
  AlertTriangle,
  Radio,
  Sliders,
  Compass,
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useIdracInstances,
  useDeleteIdracInstance,
  useIdracVitals,
  useIdracPowerAction,
  useIdracIdentify,
  useIdracBootOverride,
} from './useIdrac'
import { AddIdracModal } from './AddIdracModal'
import { ConfirmBmcPowerModal } from './ConfirmBmcPowerModal'
import { BmcFanControlModal } from './BmcFanControlModal'
import type { IdracInstanceDto } from '../../../api/idrac'

export function IdracAdaptersView() {
  const { data: instances, isLoading, refetch } = useIdracInstances()
  const deleteMutation = useDeleteIdracInstance()

  const [modalOpen, setModalOpen] = useState(false)
  const [editingInstance, setEditingInstance] = useState<IdracInstanceDto | null>(null)

  const handleDelete = async (id: string, name: string) => {
    if (confirm(`Are you sure you want to remove BMC instance '${name}'?`)) {
      await deleteMutation.mutateAsync(id)
    }
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12 text-zinc-500 text-sm">
        <RefreshCw className="h-5 w-5 animate-spin mr-2 text-purple-400" />
        Loading BMC endpoints...
      </div>
    )
  }

  if (!instances || instances.length === 0) {
    return (
      <div className="text-center p-12 bg-zinc-900/40 border border-zinc-800 rounded-xl max-w-xl mx-auto space-y-4 animate-in fade-in">
        <div className="p-3 bg-purple-500/10 border border-purple-500/20 rounded-full w-12 h-12 flex items-center justify-center mx-auto text-purple-400">
          <Cpu className="h-6 w-6" />
        </div>
        <div>
          <h3 className="text-base font-semibold text-zinc-100">No BMC / iDRAC Endpoints Connected</h3>
          <p className="text-xs text-zinc-400 mt-1.5 max-w-md mx-auto leading-relaxed">
            Register out-of-band management controllers (Dell iDRAC, HP iLO, Supermicro BMC) for remote hardware power control, fan curves, locator beacons, and thermal telemetry.
          </p>
        </div>
        <Button
          variant="primary"
          size="sm"
          onClick={() => {
            setEditingInstance(null)
            setModalOpen(true)
          }}
          className="gap-2 bg-purple-600 hover:bg-purple-500 text-white font-semibold shadow-xs"
        >
          <Plus className="h-4 w-4" />
          Connect BMC / iDRAC
        </Button>
        {modalOpen && (
          <AddIdracModal
            open={modalOpen}
            onClose={() => setModalOpen(false)}
            initialInstance={editingInstance}
          />
        )}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex items-center justify-between gap-4 bg-zinc-900/60 p-4 border border-zinc-800/80 rounded-xl">
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-purple-500/10 border border-purple-500/20 text-purple-400">
            <Cpu className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-zinc-100">Out-of-Band Hardware BMCs</h3>
            <p className="text-xs text-zinc-400">
              {instances.length} physical server{instances.length === 1 ? '' : 's'} managed via Redfish REST & IPMI
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => refetch()}
            className="text-zinc-400 hover:text-zinc-200"
            title="Refresh BMC Statuses"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </Button>
          <Button
            variant="primary"
            size="sm"
            onClick={() => {
              setEditingInstance(null)
              setModalOpen(true)
            }}
            className="gap-1.5 text-xs bg-purple-600 hover:bg-purple-500 text-white font-semibold shadow-xs"
          >
            <Plus className="h-3.5 w-3.5" />
            Connect BMC
          </Button>
        </div>
      </div>

      {/* BMC Cards Grid */}
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
        {instances.map((instance) => (
          <BmcServerCard
            key={instance.id}
            instance={instance}
            onEdit={() => {
              setEditingInstance(instance)
              setModalOpen(true)
            }}
            onDelete={() => handleDelete(instance.id, instance.name)}
          />
        ))}
      </div>

      {/* Edit / Add Modal */}
      {modalOpen && (
        <AddIdracModal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          initialInstance={editingInstance}
        />
      )}
    </div>
  )
}

function BmcServerCard({
  instance,
  onEdit,
  onDelete,
}: {
  instance: IdracInstanceDto
  onEdit: () => void
  onDelete: () => void
}) {
  const { data: vitals, isLoading, refetch } = useIdracVitals(instance.id)
  const powerMutation = useIdracPowerAction(instance.id)
  const identifyMutation = useIdracIdentify(instance.id)
  const bootOverrideMutation = useIdracBootOverride(instance.id)

  const [powerModalConfig, setPowerModalConfig] = useState<{ open: boolean; action: string; label: string } | null>(null)
  const [fanModalOpen, setFanModalOpen] = useState(false)
  const [isBlinking, setIsBlinking] = useState(false)
  const [bootMsg, setBootMsg] = useState<string | null>(null)
  const [bootSelectOpen, setBootSelectOpen] = useState(false)

  const isPowerOn = vitals?.powerState?.toLowerCase() === 'on'

  const handleBlinkLed = async () => {
    setIsBlinking(true)
    try {
      await identifyMutation.mutateAsync({ state: 'Blink', durationSeconds: 15 })
      setTimeout(() => setIsBlinking(false), 15000)
    } catch {
      setIsBlinking(false)
    }
  }

  const handleBootOverride = async (target: string, label: string) => {
    setBootSelectOpen(false)
    try {
      await bootOverrideMutation.mutateAsync({ target })
      setBootMsg(`Next boot: ${label}`)
      setTimeout(() => setBootMsg(null), 4000)
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'Failed to set boot override.')
    }
  }

  return (
    <div className="p-5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-4 hover:border-zinc-700/60 transition-all flex flex-col justify-between">
      {/* Card Header */}
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-bold text-sm text-zinc-100 truncate">
              {instance.name}
            </span>
            <Badge
              variant={isPowerOn ? 'success' : 'destructive'}
              className="text-[10px] py-0"
            >
              {vitals?.powerState || (isLoading ? 'Querying...' : 'Unknown')}
            </Badge>
            {instance.connectionMode === 'agent' ? (
              <Badge variant="outline" className="text-[10px] py-0 border-purple-500/40 text-purple-300 gap-1 bg-purple-950/20">
                In-Band IPMI
              </Badge>
            ) : (
              <Badge variant="outline" className="text-[10px] py-0 border-blue-500/40 text-blue-300 gap-1 bg-blue-950/20">
                Redfish
              </Badge>
            )}
            {isBlinking && (
              <Badge variant="outline" className="text-[10px] py-0 border-amber-500 text-amber-300 gap-1 animate-pulse bg-amber-950/30">
                <Radio className="h-2.5 w-2.5" />
                UID Blinking
              </Badge>
            )}
          </div>
          <div className="flex items-center gap-2 text-xs text-zinc-400 font-mono flex-wrap">
            {instance.connectionMode === 'agent' && instance.hostName && (
              <>
                <span className="text-purple-300 font-medium font-sans">Host: {instance.hostName}</span>
                <span className="text-zinc-600">•</span>
              </>
            )}
            <span className="font-semibold text-zinc-200">
              {vitals?.model || (instance.connectionMode === 'agent' ? 'Baremetal IPMI' : 'Dell PowerEdge')}
            </span>
            {vitals?.serialNumber && (
              <>
                <span className="text-zinc-600">•</span>
                <span className="text-zinc-400">Tag: {vitals.serialNumber}</span>
              </>
            )}
            {vitals?.biosVersion && (
              <>
                <span className="text-zinc-600">•</span>
                <span className="text-zinc-300 font-sans">BIOS {vitals.biosVersion}</span>
              </>
            )}
            {vitals?.bmcFirmwareVersion && (
              <>
                <span className="text-zinc-600">•</span>
                <span className="text-purple-300/80 font-sans">BMC {vitals.bmcFirmwareVersion}</span>
              </>
            )}
          </div>
        </div>

        <div className="flex items-center gap-1.5 shrink-0">
          {/* Fan Control Button */}
          <button
            onClick={() => setFanModalOpen(true)}
            className="p-1.5 rounded-lg text-sky-400 hover:text-sky-200 hover:bg-sky-950/40 border border-sky-500/20"
            title="Configure Fan Speeds & Curve"
          >
            <Sliders className="h-3.5 w-3.5" />
          </button>

          {/* Locator LED (UID) */}
          <button
            onClick={handleBlinkLed}
            disabled={identifyMutation.isPending}
            className={`p-1.5 rounded-lg border transition-all ${
              isBlinking
                ? 'bg-amber-500/20 text-amber-300 border-amber-500/60 animate-pulse'
                : 'text-zinc-400 hover:text-amber-300 hover:bg-zinc-800/60 border-zinc-800'
            }`}
            title="Blink Chassis Locator LED (UID) for 15s"
          >
            <Radio className="h-3.5 w-3.5" />
          </button>

          {instance.connectionMode !== 'agent' && instance.bmcUrl.startsWith('http') && (
            <a
              href={instance.bmcUrl}
              target="_blank"
              rel="noreferrer"
              className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60"
              title="Open BMC Web Interface"
            >
              <ExternalLink className="h-3.5 w-3.5" />
            </a>
          )}
          <button
            onClick={() => refetch()}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60"
            title="Refresh Vitals"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </button>
          <button
            onClick={onEdit}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60"
            title="Edit Configuration"
          >
            <Edit2 className="h-3.5 w-3.5" />
          </button>
          <button
            onClick={onDelete}
            className="p-1.5 rounded-lg text-red-400 hover:text-red-300 hover:bg-red-950/30"
            title="Remove BMC"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* Telemetry Strip */}
      <div className="grid grid-cols-2 sm:grid-cols-3 gap-2.5 p-3 bg-zinc-900/40 border border-zinc-800/60 rounded-lg text-xs">
        {/* Power Draw */}
        <div className="space-y-0.5">
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider flex items-center gap-1">
            <Zap className="h-3 w-3 text-amber-400" />
            Power Consumption
          </span>
          <p className="font-mono text-sm font-semibold text-zinc-100">
            {vitals?.powerConsumptionWatts != null ? `${vitals.powerConsumptionWatts.toFixed(0)} W` : 'N/A'}
          </p>
        </div>

        {/* Primary Temp */}
        <div className="space-y-0.5">
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider flex items-center gap-1">
            <Thermometer className="h-3 w-3 text-red-400" />
            Chassis Temp
          </span>
          <p className="font-mono text-sm font-semibold text-zinc-100">
            {vitals?.temperatures && vitals.temperatures.length > 0
              ? `${vitals.temperatures[0].currentReadingCelsius.toFixed(1)} °C`
              : 'N/A'}
          </p>
        </div>

        {/* Fan Status (Interactive) */}
        <button
          type="button"
          onClick={() => setFanModalOpen(true)}
          className="space-y-0.5 text-left p-1 -m-1 rounded hover:bg-zinc-800/40 transition-colors group cursor-pointer"
          title="Click to adjust fan speed"
        >
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider flex items-center justify-between gap-1 group-hover:text-sky-300">
            <span className="flex items-center gap-1">
              <Fan className="h-3 w-3 text-sky-400 group-hover:animate-spin" />
              Fans
            </span>
            <span className="text-[9px] text-sky-400 opacity-80 group-hover:opacity-100">Tune</span>
          </span>
          <p className="font-mono text-sm font-semibold text-zinc-100 group-hover:text-sky-200">
            {vitals?.fans && vitals.fans.length > 0
              ? `${vitals.fans[0].readingRpm} RPM`
              : 'N/A'}
          </p>
        </button>
      </div>

      {/* Sensor Vitals Lists (if available) */}
      {vitals?.temperatures && vitals.temperatures.length > 1 && (
        <div className="space-y-1 text-xs">
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider font-semibold">
            Thermal Sensors
          </span>
          <div className="grid grid-cols-2 gap-2">
            {vitals.temperatures.slice(0, 4).map((sensor) => {
              const isWarning = sensor.currentReadingCelsius > 75
              return (
                <div
                  key={sensor.name}
                  className="flex items-center justify-between p-2 rounded bg-zinc-900/30 border border-zinc-800/40"
                >
                  <span className="text-zinc-400 truncate text-[11px]">{sensor.name}</span>
                  <span
                    className={`font-mono text-[11px] font-bold ${
                      isWarning ? 'text-red-400' : 'text-zinc-200'
                    }`}
                  >
                    {sensor.currentReadingCelsius}°C
                  </span>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Remote Actions Footer */}
      <div className="pt-3 border-t border-zinc-800/80 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5 text-[11px] text-zinc-500 font-mono">
            <ShieldCheck className="h-3.5 w-3.5 text-purple-400" />
            <span>
              {instance.connectionMode === 'agent'
                ? (instance.hostName ? `In-Band IPMI (${instance.hostName})` : 'In-Band Host IPMI')
                : (instance.hostnameOrIp || instance.bmcUrl)}
            </span>
          </div>

          {/* One-Time Boot Device Trigger */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setBootSelectOpen(!bootSelectOpen)}
              className="text-[10px] font-medium text-zinc-400 hover:text-zinc-200 px-2 py-0.5 rounded border border-zinc-800 hover:border-zinc-700 bg-zinc-900/40 flex items-center gap-1"
              title="Set one-time boot override target"
            >
              <Compass className="h-2.5 w-2.5 text-purple-400" />
              {bootMsg || 'Next Boot...'}
            </button>

            {bootSelectOpen && (
              <div className="absolute left-0 bottom-7 z-20 w-44 bg-zinc-900 border border-zinc-700 rounded-lg shadow-xl p-1 space-y-0.5 text-xs animate-in fade-in zoom-in-95">
                <div className="px-2 py-1 text-[10px] font-semibold text-zinc-500 uppercase tracking-wider border-b border-zinc-800">
                  One-Time Boot Device
                </div>
                <button
                  type="button"
                  onClick={() => handleBootOverride('BiosSetup', 'BIOS Setup')}
                  className="w-full text-left px-2 py-1.5 rounded hover:bg-purple-950/40 hover:text-purple-300 text-zinc-300 text-[11px] flex items-center justify-between"
                >
                  <span>BIOS / System Setup</span>
                </button>
                <button
                  type="button"
                  onClick={() => handleBootOverride('Pxe', 'PXE Network')}
                  className="w-full text-left px-2 py-1.5 rounded hover:bg-purple-950/40 hover:text-purple-300 text-zinc-300 text-[11px] flex items-center justify-between"
                >
                  <span>PXE Network Boot</span>
                </button>
                <button
                  type="button"
                  onClick={() => handleBootOverride('Disk', 'Primary Disk')}
                  className="w-full text-left px-2 py-1.5 rounded hover:bg-purple-950/40 hover:text-purple-300 text-zinc-300 text-[11px] flex items-center justify-between"
                >
                  <span>Hard Disk</span>
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Hardware Power Control Buttons */}
        <div className="flex items-center gap-1.5">
          {!isPowerOn ? (
            <Button
              variant="primary"
              size="sm"
              onClick={() => setPowerModalConfig({ open: true, action: 'On', label: 'Power On' })}
              disabled={powerMutation.isPending}
              className="h-7 px-2.5 text-xs bg-emerald-600 hover:bg-emerald-500 text-white font-semibold gap-1"
            >
              <Power className="h-3 w-3" />
              Power On
            </Button>
          ) : (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPowerModalConfig({ open: true, action: 'GracefulShutdown', label: 'Graceful Shutdown' })}
                disabled={powerMutation.isPending}
                className="h-7 px-2 text-xs border-zinc-700 text-zinc-300 hover:text-white hover:bg-zinc-800 gap-1"
                title="Send ACPI OS shutdown command"
              >
                <PowerOff className="h-3 w-3 text-amber-400" />
                Shutdown
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPowerModalConfig({ open: true, action: 'PowerCycle', label: 'Cold Power Cycle' })}
                disabled={powerMutation.isPending}
                className="h-7 px-2 text-xs border-zinc-700 text-zinc-300 hover:text-white hover:bg-zinc-800 gap-1"
                title="Cold chassis power cycle"
              >
                <RotateCcw className="h-3 w-3 text-sky-400" />
                Power Cycle
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPowerModalConfig({ open: true, action: 'ForceOff', label: 'Immediate Force Off' })}
                disabled={powerMutation.isPending}
                className="h-7 px-2 text-xs border-red-900/60 text-red-400 hover:bg-red-950/40 hover:text-red-300 gap-1"
                title="Immediate hardware power cutoff"
              >
                <AlertTriangle className="h-3 w-3" />
                Force Off
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Power Confirmation Dialog Modal */}
      {powerModalConfig && (
        <ConfirmBmcPowerModal
          open={powerModalConfig.open}
          onClose={() => setPowerModalConfig(null)}
          targetName={instance.name}
          targetIp={instance.connectionMode === 'agent' ? (instance.hostName || undefined) : (instance.hostnameOrIp || instance.bmcUrl)}
          action={powerModalConfig.action}
          actionLabel={powerModalConfig.label}
          onConfirm={async () => {
            await powerMutation.mutateAsync(powerModalConfig.action)
          }}
        />
      )}

      {/* Fan Control Modal */}
      {fanModalOpen && (
        <BmcFanControlModal
          open={fanModalOpen}
          onClose={() => setFanModalOpen(false)}
          instanceId={instance.id}
          serverName={instance.name}
          currentFans={vitals?.fans || []}
        />
      )}
    </div>
  )
}

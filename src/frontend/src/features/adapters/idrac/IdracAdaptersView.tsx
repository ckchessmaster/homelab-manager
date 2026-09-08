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
} from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import {
  useIdracInstances,
  useDeleteIdracInstance,
  useIdracVitals,
  useIdracPowerAction,
} from './useIdrac'
import { AddIdracModal } from './AddIdracModal'
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
            Register out-of-band management controllers (Dell iDRAC, HP iLO, Supermicro BMC) for remote hardware power control, power draw monitoring, and thermal telemetry.
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
              {instances.length} physical server{instances.length === 1 ? '' : 's'} managed via Redfish REST
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

  const isPowerOn = vitals?.powerState?.toLowerCase() === 'on'

  const handlePowerAction = async (action: string, label: string) => {
    if (confirm(`Send hardware command '${label}' to ${instance.name}?`)) {
      await powerMutation.mutateAsync(action)
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
          </div>
          <div className="flex items-center gap-2 text-xs text-zinc-400 font-mono">
            <span>{vitals?.model || 'Dell PowerEdge'}</span>
            {vitals?.serialNumber && (
              <>
                <span className="text-zinc-600">•</span>
                <span className="text-zinc-400">Tag: {vitals.serialNumber}</span>
              </>
            )}
            {vitals?.biosVersion && (
              <>
                <span className="text-zinc-600">•</span>
                <span className="text-zinc-500">BIOS {vitals.biosVersion}</span>
              </>
            )}
          </div>
        </div>

        <div className="flex items-center gap-1.5 shrink-0">
          <a
            href={instance.bmcUrl}
            target="_blank"
            rel="noreferrer"
            className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60"
            title="Open BMC Web Interface"
          >
            <ExternalLink className="h-3.5 w-3.5" />
          </a>
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

        {/* Fan Status */}
        <div className="space-y-0.5">
          <span className="text-[10px] text-zinc-500 uppercase tracking-wider flex items-center gap-1">
            <Fan className="h-3 w-3 text-sky-400" />
            Fans
          </span>
          <p className="font-mono text-sm font-semibold text-zinc-100">
            {vitals?.fans && vitals.fans.length > 0
              ? `${vitals.fans[0].readingRpm} RPM`
              : 'N/A'}
          </p>
        </div>
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

      {/* Remote Power Actions Footer */}
      <div className="pt-3 border-t border-zinc-800/80 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-1.5 text-[11px] text-zinc-500 font-mono">
          <ShieldCheck className="h-3.5 w-3.5 text-purple-400" />
          <span>{instance.hostnameOrIp || instance.bmcUrl}</span>
        </div>

        <div className="flex items-center gap-1.5">
          {!isPowerOn ? (
            <Button
              variant="primary"
              size="sm"
              onClick={() => handlePowerAction('On', 'Power On')}
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
                onClick={() => handlePowerAction('GracefulShutdown', 'Graceful ACPI Shutdown')}
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
                onClick={() => handlePowerAction('PowerCycle', 'Cold Power Cycle')}
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
                onClick={() => handlePowerAction('ForceOff', 'Immediate Force Off')}
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
    </div>
  )
}

import { useState } from 'react'
import { Badge } from '../../../components/ui/badge'
import { Button } from '../../../components/ui/button'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import {
  Zap,
  RotateCcw,
  Radio,
  CheckCircle2,
  AlertCircle,
  Activity,
  AlertTriangle,
  Loader2,
} from 'lucide-react'
import { usePowerCycleUniFiPort } from './useUniFi'
import type { UniFiPortDto } from '../../../api/unifi'

interface SwitchPortVisualizerProps {
  instanceId: string
  deviceMac: string
  deviceName: string
  ports: UniFiPortDto[]
}

export function SwitchPortVisualizer({
  instanceId,
  deviceMac,
  deviceName,
  ports,
}: SwitchPortVisualizerProps) {
  const powerCycleMutation = usePowerCycleUniFiPort(instanceId)
  const [bouncingPort, setBouncingPort] = useState<number | null>(null)
  const [confirmPort, setConfirmPort] = useState<{ portIdx: number; portName?: string | null } | null>(null)
  const [actionMessage, setActionMessage] = useState<{ port: number; success: boolean; text: string } | null>(null)

  const handlePowerCycle = async (portIdx: number) => {
    setBouncingPort(portIdx)
    setActionMessage(null)
    try {
      const res = await powerCycleMutation.mutateAsync({
        deviceMac,
        portIdx,
        delaySeconds: 5,
      })
      setActionMessage({
        port: portIdx,
        success: res.success,
        text: res.message || `Port ${portIdx} PoE cycle completed.`,
      })
    } catch (err: any) {
      setActionMessage({
        port: portIdx,
        success: false,
        text: err.message || `Failed to cycle PoE on port ${portIdx}`,
      })
    } finally {
      setBouncingPort(null)
    }
  }

  if (!ports || ports.length === 0) {
    return (
      <div className="p-4 text-center text-sm text-muted-foreground bg-slate-900/40 rounded-lg border border-slate-800">
        No switch ports reported by UniFi for {deviceName}.
      </div>
    )
  }

  const totalPoEWatts = ports.reduce((acc, p) => acc + (p.poePowerWatts || 0), 0)
  const activePorts = ports.filter((p) => p.up).length

  return (
    <div className="space-y-4">
      {/* Header bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-900/60 rounded-lg border border-slate-800/80">
        <div className="flex items-center gap-3">
          <Activity className="h-4 w-4 text-blue-400" />
          <span className="text-xs font-semibold text-slate-200">
            {ports.length}-Port Switch Matrix ({deviceName})
          </span>
        </div>
        <div className="flex items-center gap-3 text-xs">
          <Badge variant="outline" className="bg-slate-800/40 border-slate-700 text-slate-300">
            {activePorts} / {ports.length} Connected
          </Badge>
          {totalPoEWatts > 0 && (
            <Badge variant="outline" className="bg-amber-500/10 border-amber-500/30 text-amber-300 flex items-center gap-1">
              <Zap className="h-3 w-3 text-amber-400 fill-amber-400/20" />
              <span>{totalPoEWatts.toFixed(1)} W PoE Total</span>
            </Badge>
          )}
        </div>
      </div>

      {actionMessage && (
        <div
          className={`p-2.5 rounded-lg border text-xs flex items-center gap-2 ${
            actionMessage.success
              ? 'bg-emerald-950/30 border-emerald-800/50 text-emerald-300'
              : 'bg-red-950/30 border-red-800/50 text-red-300'
          }`}
        >
          {actionMessage.success ? (
            <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400" />
          ) : (
            <AlertCircle className="h-4 w-4 shrink-0 text-red-400" />
          )}
          <span>{actionMessage.text}</span>
        </div>
      )}

      {/* Switch faceplate grid */}
      <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 lg:grid-cols-8 gap-2.5">
        {ports.map((port) => {
          const isBouncing = bouncingPort === port.portIdx
          const hasPoE = port.poeMode && port.poeMode !== 'off'
          const isDrawingPower = (port.poePowerWatts ?? 0) > 0

          return (
            <div
              key={port.portIdx}
              className={`relative flex flex-col justify-between p-2.5 rounded-lg border transition-all ${
                port.up
                  ? 'bg-slate-900/90 border-slate-700/80 shadow-sm'
                  : 'bg-slate-950/60 border-slate-800/60 opacity-75'
              }`}
            >
              {/* Top: Port Number & Link LED */}
              <div className="flex items-center justify-between mb-1.5">
                <span className="text-xs font-mono font-bold text-slate-300">
                  #{port.portIdx}
                </span>
                <div className="flex items-center gap-1">
                  <div
                    className={`h-2 w-2 rounded-full ${
                      port.up
                        ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]'
                        : 'bg-slate-700'
                    }`}
                    title={port.up ? `Up (${port.speedMbps ?? 1000} Mbps)` : 'Link Down'}
                  />
                  {hasPoE && (
                    <span title={`PoE Mode: ${port.poeMode}${isDrawingPower ? ` (${port.poePowerWatts?.toFixed(1)}W)` : ''}`}>
                      <Zap
                        className={`h-3 w-3 ${
                          isDrawingPower
                            ? 'text-amber-400 fill-amber-400'
                            : 'text-slate-600'
                        }`}
                      />
                    </span>
                  )}
                </div>
              </div>

              {/* Port Profile / Name */}
              <div className="text-[11px] font-medium text-slate-400 truncate mb-2" title={port.name || `Port ${port.portIdx}`}>
                {port.name || `Port ${port.portIdx}`}
              </div>

              {/* Port Metrics */}
              <div className="text-[10px] space-y-0.5 text-slate-400 mb-2">
                {port.up ? (
                  <div className="text-emerald-400 font-mono font-medium">
                    {port.speedMbps ? (port.speedMbps >= 1000 ? `${port.speedMbps / 1000}G` : `${port.speedMbps}M`) : 'Up'}
                  </div>
                ) : (
                  <div className="text-slate-500">Disconnected</div>
                )}
                {isDrawingPower && (
                  <div className="text-amber-300 font-mono">
                    {port.poePowerWatts?.toFixed(1)} W
                  </div>
                )}
              </div>

              {/* Action Button: Cycle PoE */}
              {hasPoE ? (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={isBouncing || powerCycleMutation.isPending}
                  onClick={() => setConfirmPort({ portIdx: port.portIdx, portName: port.name })}
                  className="w-full h-6 text-[10px] px-1.5 gap-1 border-slate-700 bg-slate-800/40 hover:bg-slate-800 text-slate-300 hover:text-white"
                  title="Power cycle PoE to reboot attached hardware"
                >
                  {isBouncing ? (
                    <Radio className="h-3 w-3 animate-spin text-amber-400" />
                  ) : (
                    <RotateCcw className="h-3 w-3 text-amber-400" />
                  )}
                  <span>Cycle PoE</span>
                </Button>
              ) : (
                <div className="h-6 flex items-center justify-center text-[10px] text-slate-600">
                  Data only
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* PoE Cycle Confirmation Dialog */}
      {confirmPort && (
        <Dialog open={true} onClose={() => setConfirmPort(null)} maxWidth="md">
          <DialogHeader onClose={() => setConfirmPort(null)}>
            <div className="flex items-center gap-2 text-amber-400">
              <AlertTriangle className="h-5 w-5 shrink-0" />
              <DialogTitle className="text-zinc-100">
                Confirm PoE Power Cycle
              </DialogTitle>
            </div>
          </DialogHeader>
          <DialogBody className="space-y-4">
            <p className="text-sm text-zinc-300">
              Are you sure you want to cycle PoE power on{' '}
              <strong className="text-white font-semibold">
                Port {confirmPort.portIdx}
                {confirmPort.portName ? ` (${confirmPort.portName})` : ''}
              </strong>{' '}
              on switch <span className="font-mono text-zinc-200">{deviceName}</span>?
            </p>
            <div className="p-3 bg-amber-950/30 border border-amber-800/40 rounded-lg text-xs text-amber-200/90 leading-relaxed">
              This will drop power for 5 seconds, forcing any connected Access Point, camera, or server to hard-reboot.
            </div>
          </DialogBody>
          <DialogFooter>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setConfirmPort(null)}
              disabled={powerCycleMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={async () => {
                const portIdx = confirmPort.portIdx
                setConfirmPort(null)
                await handlePowerCycle(portIdx)
              }}
              disabled={powerCycleMutation.isPending}
              className="bg-amber-600 hover:bg-amber-500 text-white font-semibold gap-1.5"
            >
              {powerCycleMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <RotateCcw className="h-4 w-4" />
              )}
              Cycle Power
            </Button>
          </DialogFooter>
        </Dialog>
      )}
    </div>
  )
}

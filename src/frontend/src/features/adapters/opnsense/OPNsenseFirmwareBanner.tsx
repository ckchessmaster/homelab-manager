import { RefreshCw, ExternalLink, AlertTriangle, CheckCircle2, PackageCheck } from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { Badge } from '../../../components/ui/badge'
import { useOPNsenseFirmware, useCheckOPNsenseFirmware } from './useOPNsense'

interface OPNsenseFirmwareBannerProps {
  instanceId: string
  baseUrl: string
}

export function OPNsenseFirmwareBanner({ instanceId, baseUrl }: OPNsenseFirmwareBannerProps) {
  const { data: firmware, isLoading } = useOPNsenseFirmware(instanceId)
  const checkMutation = useCheckOPNsenseFirmware(instanceId)

  if (isLoading || !firmware) {
    return null
  }

  const hasUpdates = firmware.updatesAvailable > 0
  const webGuiFirmwareUrl = `${baseUrl.replace(/\/+$/, '')}/ui/core/firmware`

  return (
    <div
      className={`p-4 rounded-xl border transition-all ${
        hasUpdates
          ? 'bg-amber-500/10 border-amber-500/30 text-amber-200'
          : 'bg-zinc-900/60 border-zinc-800 text-zinc-300'
      }`}
    >
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-start sm:items-center gap-3">
          <div
            className={`p-2 rounded-lg shrink-0 ${
              hasUpdates
                ? 'bg-amber-500/20 text-amber-400 border border-amber-500/30'
                : 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
            }`}
          >
            {hasUpdates ? (
              <AlertTriangle className="h-5 w-5" />
            ) : (
              <CheckCircle2 className="h-5 w-5" />
            )}
          </div>

          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold text-zinc-100">
                {hasUpdates
                  ? `${firmware.updatesAvailable} Update${firmware.updatesAvailable > 1 ? 's' : ''} Available`
                  : 'Firmware Up to Date'}
              </span>
              <Badge variant={hasUpdates ? 'warning' : 'success'} className="text-[10px] py-0">
                {firmware.version || 'OPNsense'}
              </Badge>
              {firmware.needsReboot && (
                <Badge variant="destructive" className="text-[10px] py-0 animate-pulse">
                  Reboot Required
                </Badge>
              )}
            </div>

            <p className="text-xs text-zinc-400">
              {firmware.statusMsg ||
                (hasUpdates
                  ? 'System and package updates are ready to be installed.'
                  : 'All core modules and plugins are running current versions.')}
              {firmware.lastCheck && (
                <span className="text-zinc-500 ml-1.5 font-mono text-[11px]">
                  (Checked: {new Date(firmware.lastCheck).toLocaleDateString()})
                </span>
              )}
            </p>

            {hasUpdates && firmware.packages && firmware.packages.length > 0 && (
              <div className="flex flex-wrap gap-1.5 pt-1">
                {firmware.packages.slice(0, 6).map((pkg) => (
                  <span
                    key={pkg}
                    className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[11px] font-mono bg-zinc-950/70 border border-zinc-800 text-zinc-300"
                  >
                    <PackageCheck className="h-3 w-3 text-amber-400" />
                    {pkg}
                  </span>
                ))}
                {firmware.packages.length > 6 && (
                  <span className="text-[11px] text-zinc-500 self-center">
                    +{firmware.packages.length - 6} more
                  </span>
                )}
              </div>
            )}
          </div>
        </div>

        <div className="flex items-center gap-2 shrink-0 self-end sm:self-center">
          <Button
            variant="outline"
            size="sm"
            onClick={() => checkMutation.mutate()}
            disabled={checkMutation.isPending}
            className="gap-1.5 text-xs h-8 border-zinc-700 bg-zinc-900/80 hover:bg-zinc-800 text-zinc-200"
            title="Trigger check for updates"
          >
            <RefreshCw
              className={`h-3.5 w-3.5 text-orange-400 ${checkMutation.isPending ? 'animate-spin' : ''}`}
            />
            <span>{checkMutation.isPending ? 'Checking...' : 'Check for Updates'}</span>
          </Button>

          {hasUpdates && (
            <a
              href={webGuiFirmwareUrl}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-orange-600 hover:bg-orange-500 text-white shadow-xs transition-colors h-8"
            >
              <span>Upgrade via WebGUI</span>
              <ExternalLink className="h-3.5 w-3.5" />
            </a>
          )}
        </div>
      </div>
    </div>
  )
}

import { useState, useEffect } from 'react'
import { Sliders, ShieldAlert, Check, RefreshCw } from 'lucide-react'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { useHardwareThresholds, useUpdateHardwareThresholds } from '../hosts/useHosts'
import type { HardwareThresholds } from '../../api/hosts'

export function HardwareThresholdsCard() {
  const { data: thresholds, isLoading, isError } = useHardwareThresholds()
  const updateMutation = useUpdateHardwareThresholds()

  const [form, setForm] = useState<HardwareThresholds>({
    minSsdWearOutPct: 10.0,
    criticalSsdWearOutPct: 2.0,
    maxDiskTemperatureCelsius: 55.0,
    criticalDiskTemperatureCelsius: 65.0,
    alertOnPsuRedundancyLost: true,
    alertOnEccErrors: true,
  })

  const [savedSuccess, setSavedSuccess] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    if (thresholds) {
      setForm(thresholds)
    }
  }, [thresholds])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSavedSuccess(false)
    setErrorMessage(null)

    try {
      await updateMutation.mutateAsync(form)
      setSavedSuccess(true)
      setTimeout(() => setSavedSuccess(false), 3000)
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to update hardware thresholds')
    }
  }

  if (isLoading) {
    return (
      <div className="p-8 text-center border border-zinc-800 rounded-xl bg-zinc-900/40">
        <RefreshCw className="h-5 w-5 animate-spin mx-auto text-sky-400 mb-2" />
        <p className="text-xs text-zinc-400">Loading hardware health thresholds...</p>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="p-6 text-center border border-rose-800/60 rounded-xl bg-rose-950/20 text-rose-300">
        <p className="text-xs font-medium">Failed to load hardware health thresholds.</p>
      </div>
    )
  }

  return (
    <form onSubmit={handleSubmit} className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-zinc-800 pb-4">
        <div>
          <h3 className="text-base font-semibold text-zinc-100 flex items-center gap-2">
            <Sliders className="h-5 w-5 text-sky-400" />
            Hardware Health & Alert Thresholds
          </h3>
          <p className="text-xs text-zinc-400 mt-0.5">
            Configure telemetry triggers for disk wear-out, drive thermal limits, and fault alerts across the fleet.
          </p>
        </div>

        <Button
          type="submit"
          variant="primary"
          size="sm"
          disabled={updateMutation.isPending}
          className="gap-1.5 bg-sky-500 hover:bg-sky-400 text-zinc-950 font-semibold text-xs shrink-0 self-start sm:self-auto cursor-pointer"
        >
          {updateMutation.isPending ? (
            <RefreshCw className="h-3.5 w-3.5 animate-spin" />
          ) : savedSuccess ? (
            <Check className="h-3.5 w-3.5 text-zinc-950" />
          ) : (
            <ShieldAlert className="h-3.5 w-3.5" />
          )}
          {updateMutation.isPending ? 'Saving...' : savedSuccess ? 'Saved!' : 'Save Thresholds'}
        </Button>
      </div>

      {savedSuccess && (
        <div className="p-3 bg-emerald-950/40 border border-emerald-800/60 rounded-xl text-xs text-emerald-300 flex items-center gap-2 animate-in fade-in">
          <Check className="h-4 w-4 text-emerald-400 shrink-0" />
          <span>Hardware thresholds successfully updated and applied across all managed hosts.</span>
        </div>
      )}

      {errorMessage && (
        <div className="p-3 bg-rose-950/40 border border-rose-800/60 rounded-xl text-xs text-rose-300">
          {errorMessage}
        </div>
      )}

      {/* Grid of Threshold Settings */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        {/* SSD Endurance Warning */}
        <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <Input
            label="SSD Wear-Out Warning Threshold (%)"
            type="number"
            step="0.5"
            min="1"
            max="50"
            value={form.minSsdWearOutPct}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                minSsdWearOutPct: parseFloat(e.target.value) || 0,
              }))
            }
            helperText="Trigger a Hardware Warning when drive life remaining falls to or below this percentage."
          />
        </div>

        {/* SSD Endurance Critical */}
        <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <Input
            label="SSD Wear-Out Critical Threshold (%)"
            type="number"
            step="0.5"
            min="0"
            max="20"
            value={form.criticalSsdWearOutPct}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                criticalSsdWearOutPct: parseFloat(e.target.value) || 0,
              }))
            }
            helperText="Trigger a Hardware Critical alert when drive life remaining falls to or below this percentage."
          />
        </div>

        {/* Temperature Warning */}
        <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <Input
            label="Disk Warning Temperature (°C)"
            type="number"
            step="1"
            min="30"
            max="80"
            value={form.maxDiskTemperatureCelsius}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                maxDiskTemperatureCelsius: parseFloat(e.target.value) || 0,
              }))
            }
            helperText="Trigger a Warning when a physical drive or NVMe sensor reaches or exceeds this temperature."
          />
        </div>

        {/* Temperature Critical */}
        <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-2">
          <Input
            label="Disk Critical Temperature (°C)"
            type="number"
            step="1"
            min="40"
            max="95"
            value={form.criticalDiskTemperatureCelsius}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                criticalDiskTemperatureCelsius: parseFloat(e.target.value) || 0,
              }))
            }
            helperText="Trigger a Critical alert when a physical drive reaches or exceeds this temperature."
          />
        </div>
      </div>

      {/* Component Alert Toggles */}
      <div className="p-4 bg-zinc-950/60 border border-zinc-800/80 rounded-xl space-y-3">
        <span className="text-xs font-medium text-zinc-300 uppercase tracking-wider block">
          Hardware Component Alert Policies
        </span>

        <label className="flex items-center justify-between p-2 rounded-lg hover:bg-zinc-900/60 transition-colors cursor-pointer">
          <div className="space-y-0.5">
            <span className="text-xs font-semibold text-zinc-200 block">
              Power Supply (PSU) Redundancy Degradation
            </span>
            <span className="text-[11px] text-zinc-400 block">
              Alert when a dual-PSU chassis loses redundancy or when a power supply unit faults.
            </span>
          </div>
          <input
            type="checkbox"
            checked={form.alertOnPsuRedundancyLost}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                alertOnPsuRedundancyLost: e.target.checked,
              }))
            }
            className="rounded border-zinc-700 bg-zinc-900 text-sky-500 focus:ring-sky-500/20 cursor-pointer h-4.5 w-4.5 shrink-0"
          />
        </label>

        <label className="flex items-center justify-between p-2 rounded-lg hover:bg-zinc-900/60 transition-colors cursor-pointer border-t border-zinc-800/60">
          <div className="space-y-0.5">
            <span className="text-xs font-semibold text-zinc-200 block">
              ECC Memory Errors & DIMM Faults
            </span>
            <span className="text-[11px] text-zinc-400 block">
              Alert immediately on uncorrectable memory errors or elevated correctable ECC error spikes.
            </span>
          </div>
          <input
            type="checkbox"
            checked={form.alertOnEccErrors}
            onChange={(e) =>
              setForm((prev) => ({
                ...prev,
                alertOnEccErrors: e.target.checked,
              }))
            }
            className="rounded border-zinc-700 bg-zinc-900 text-sky-500 focus:ring-sky-500/20 cursor-pointer h-4.5 w-4.5 shrink-0"
          />
        </label>
      </div>
    </form>
  )
}

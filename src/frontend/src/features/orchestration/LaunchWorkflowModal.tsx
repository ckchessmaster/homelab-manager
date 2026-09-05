import React, { useState, useEffect } from 'react'
import {
  Sparkles,
  Play,
  Layers,
  ArrowUpCircle,
  RotateCcw,
  ShieldCheck,
  Camera,
  Server,
  AlertTriangle,
  HelpCircle,
} from 'lucide-react'
import { Dialog, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { usePipelines, getRecommendedPipelineId } from './usePipelines'
import { PipelineStepPreview } from './PipelineStepPreview'
import type { Host } from '../../api/hosts'
import type { PipelineProfile } from '../../api/pipelines'
import { createJob } from '../../api/jobs'

interface LaunchWorkflowModalProps {
  isOpen: boolean
  onClose: () => void
  host?: Host | null
  availableHosts?: Host[]
  onWorkflowLaunched: (jobId: string, host: Host) => void
}

function getPipelineIcon(iconName: string) {
  switch (iconName) {
    case 'Layers':
      return <Layers className="w-5 h-5 text-indigo-400" />
    case 'ArrowUpCircle':
      return <ArrowUpCircle className="w-5 h-5 text-emerald-400" />
    case 'RotateCcw':
      return <RotateCcw className="w-5 h-5 text-amber-400" />
    case 'ShieldCheck':
      return <ShieldCheck className="w-5 h-5 text-sky-400" />
    case 'Camera':
      return <Camera className="w-5 h-5 text-purple-400" />
    default:
      return <Sparkles className="w-5 h-5 text-emerald-400" />
  }
}

export const LaunchWorkflowModal: React.FC<LaunchWorkflowModalProps> = ({
  isOpen,
  onClose,
  host: initialHost = null,
  availableHosts = [],
  onWorkflowLaunched,
}) => {
  const { data: pipelines, isLoading: isLoadingPipelines } = usePipelines()

  const [selectedHostId, setSelectedHostId] = useState<string>(initialHost?.id || '')
  const [selectedPipelineId, setSelectedPipelineId] = useState<string>('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  // Current effective host
  const effectiveHost = initialHost || availableHosts.find((h) => h.id === selectedHostId) || null

  // Auto-select recommended pipeline whenever target host changes
  useEffect(() => {
    if (effectiveHost) {
      const recId = getRecommendedPipelineId(effectiveHost)
      setSelectedPipelineId(recId)
    } else if (pipelines && pipelines.length > 0 && !selectedPipelineId) {
      setSelectedPipelineId(pipelines[0].id)
    }
  }, [effectiveHost, pipelines])

  useEffect(() => {
    if (initialHost) {
      setSelectedHostId(initialHost.id)
    }
  }, [initialHost])

  const selectedProfile: PipelineProfile | undefined = pipelines?.find(
    (p) => p.id === selectedPipelineId
  )

  const recommendedId = getRecommendedPipelineId(effectiveHost)

  const handleLaunch = async () => {
    if (!effectiveHost || !selectedPipelineId) return

    setIsSubmitting(true)
    setErrorMsg(null)

    try {
      const job = await createJob(effectiveHost.id, selectedPipelineId)
      onWorkflowLaunched(job.id, effectiveHost)
      onClose()
    } catch (err: unknown) {
      const msg =
        err && typeof err === 'object' && 'response' in err
          ? (err as { response?: { data?: { message?: string } } }).response?.data?.message ??
            'Failed to start workflow'
          : 'Failed to start workflow'
      setErrorMsg(msg)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Dialog open={isOpen} onClose={onClose} maxWidth="xl">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
            <Sparkles className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Launch Orchestration Workflow</DialogTitle>
            <p className="text-xs text-zinc-400 mt-0.5">
              Select a pipeline profile tailored to your node maintenance requirements.
            </p>
          </div>
        </div>
      </DialogHeader>

      <DialogBody className="space-y-5 max-h-[75vh] overflow-y-auto pr-1">
        {errorMsg && (
          <div className="p-3 rounded-md bg-rose-500/10 border border-rose-500/20 text-xs text-rose-300 flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0 text-rose-400" />
            <span>{errorMsg}</span>
          </div>
        )}

        {/* Target Host Selection / Summary */}
        <div className="p-3.5 rounded-lg bg-zinc-950/70 border border-zinc-800 space-y-2">
          <div className="flex items-center justify-between">
            <label className="text-xs font-semibold text-zinc-300 flex items-center gap-1.5">
              <Server className="w-4 h-4 text-emerald-400" />
              Target Host
            </label>
            {effectiveHost?.targetType && (
              <Badge variant="outline" className="text-[10px] uppercase font-mono tracking-wider">
                {effectiveHost.targetType}
              </Badge>
            )}
          </div>

          {initialHost ? (
            <div className="flex items-center justify-between text-xs text-zinc-300 pt-1">
              <div>
                <span className="font-semibold text-white">{initialHost.hostname}</span>
                <span className="text-zinc-500 ml-2 font-mono">({initialHost.ipAddress})</span>
              </div>
              <span className="text-zinc-400">
                {initialHost.osFamily} &bull; {initialHost.agent?.upgradablePackagesCount ?? 0} updates pending
              </span>
            </div>
          ) : (
            <select
              value={selectedHostId}
              onChange={(e) => setSelectedHostId(e.target.value)}
              className="w-full px-3 py-2 text-xs bg-zinc-900 border border-zinc-700 rounded-md text-zinc-200 focus:outline-none focus:ring-1 focus:ring-emerald-500"
            >
              <option value="">-- Select Target Managed Host --</option>
              {availableHosts
                .filter((h) => h.agent?.installed)
                .map((h) => (
                  <option key={h.id} value={h.id}>
                    {h.hostname} ({h.ipAddress}) — {h.targetType || 'host'} ({h.agent?.upgradablePackagesCount || 0} updates)
                  </option>
                ))}
            </select>
          )}
        </div>

        {/* Pipeline Profile Cards Grid */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <label className="text-xs font-semibold text-zinc-300">
              Select Workflow Pipeline
            </label>
            <span className="text-[11px] text-zinc-500 flex items-center gap-1">
              <HelpCircle className="w-3 h-3" />
              Click a profile to preview its execution DAG
            </span>
          </div>

          {isLoadingPipelines ? (
            <div className="py-8 text-center text-xs text-zinc-500">
              Loading available pipeline profiles...
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
              {pipelines?.map((profile) => {
                const isSelected = selectedPipelineId === profile.id
                const isRecommended = profile.id === recommendedId

                return (
                  <button
                    key={profile.id}
                    type="button"
                    onClick={() => setSelectedPipelineId(profile.id)}
                    className={`text-left p-3 rounded-lg border transition-all relative ${
                      isSelected
                        ? 'bg-zinc-900/90 border-emerald-500/80 ring-1 ring-emerald-500/50 shadow-md shadow-emerald-950/20'
                        : 'bg-zinc-950/50 border-zinc-800/80 hover:bg-zinc-900/40 hover:border-zinc-700'
                    }`}
                  >
                    {isRecommended && (
                      <span className="absolute top-2.5 right-2.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        Recommended
                      </span>
                    )}

                    <div className="flex items-start gap-3">
                      <div
                        className={`p-2 rounded-md shrink-0 ${
                          isSelected ? 'bg-zinc-800' : 'bg-zinc-900 border border-zinc-800'
                        }`}
                      >
                        {getPipelineIcon(profile.icon)}
                      </div>
                      <div className="flex-1 min-w-0 pr-14">
                        <div className="text-xs font-semibold text-zinc-200">
                          {profile.name}
                        </div>
                        <p className="text-[11px] text-zinc-400 mt-1 line-clamp-2 leading-relaxed">
                          {profile.description}
                        </p>
                        <div className="mt-2.5 flex items-center gap-2">
                          <span className="text-[10px] font-medium px-2 py-0.5 rounded bg-zinc-800/70 text-zinc-400 border border-zinc-700/50">
                            {profile.steps.length} Steps
                          </span>
                        </div>
                      </div>
                    </div>
                  </button>
                )
              })}
            </div>
          )}
        </div>

        {/* Live Step Sequence Preview */}
        {selectedProfile && (
          <div className="p-3.5 rounded-lg bg-zinc-950/80 border border-zinc-800/80">
            <PipelineStepPreview steps={selectedProfile.steps} />
          </div>
        )}
      </DialogBody>

      <DialogFooter>
        <Button variant="secondary" size="sm" onClick={onClose} disabled={isSubmitting}>
          Cancel
        </Button>
        <Button
          variant="primary"
          size="sm"
          disabled={!effectiveHost || !selectedPipelineId || isSubmitting}
          onClick={handleLaunch}
          className="gap-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold shadow-xs"
        >
          <Play className="w-3.5 h-3.5 fill-current" />
          {isSubmitting ? 'Launching...' : 'Start Workflow'}
        </Button>
      </DialogFooter>
    </Dialog>
  )
}

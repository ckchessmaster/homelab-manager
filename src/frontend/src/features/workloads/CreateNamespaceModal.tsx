import React, { useState, useEffect } from 'react'
import {
  Layers,
  Plus,
  Trash2,
  Loader2,
  AlertTriangle,
  CheckCircle2,
  Tag,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { useCreateNamespace } from './useWorkloads'

interface CreateNamespaceModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  availableClusters?: string[]
  onSuccess?: (createdNamespace: string) => void
}

interface KeyValueItem {
  id: string
  key: string
  value: string
}

export function CreateNamespaceModal({
  open,
  onClose,
  clusterId,
  availableClusters = [],
  onSuccess,
}: CreateNamespaceModalProps) {
  const [selectedCluster, setSelectedCluster] = useState(clusterId || (availableClusters[0] ?? ''))
  const [name, setName] = useState('')
  const [labels, setLabels] = useState<KeyValueItem[]>([])
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    if (!selectedCluster && (clusterId || availableClusters.length > 0)) {
      setSelectedCluster(clusterId || availableClusters[0] || '')
    }
  }, [clusterId, availableClusters, selectedCluster])

  const createMutation = useCreateNamespace()

  // DNS-1123 label validation: 1-63 chars, lowercase alphanumeric or hyphens, start/end alphanumeric
  const trimmedName = name.trim()
  const isNameValid =
    trimmedName.length > 0 &&
    trimmedName.length <= 63 &&
    /^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/.test(trimmedName)

  const handleAddLabel = () => {
    setLabels([...labels, { id: crypto.randomUUID(), key: '', value: '' }])
  }

  const handleRemoveLabel = (id: string) => {
    setLabels(labels.filter((l) => l.id !== id))
  }

  const handleLabelChange = (id: string, field: 'key' | 'value', val: string) => {
    setLabels(
      labels.map((l) => (l.id === id ? { ...l, [field]: val } : l))
    )
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!isNameValid) return

    setErrorMessage(null)

    // Build labels dictionary
    const labelsDict: Record<string, string> = {}
    for (const item of labels) {
      if (item.key.trim()) {
        labelsDict[item.key.trim()] = item.value.trim()
      }
    }

    const targetCluster = selectedCluster || clusterId || availableClusters[0] || ''

    try {
      await createMutation.mutateAsync({
        clusterId: targetCluster,
        name: trimmedName,
        labels: Object.keys(labelsDict).length > 0 ? labelsDict : undefined,
      })

      const created = trimmedName
      setName('')
      setLabels([])
      onSuccess?.(created)
      onClose()
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to create namespace')
    }
  }

  const handleClose = () => {
    setName('')
    setLabels([])
    setErrorMessage(null)
    onClose()
  }

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md">
      <form onSubmit={handleSubmit}>
        <DialogHeader onClose={handleClose}>
          <div className="flex items-center gap-2 text-zinc-100">
            <div className="p-1.5 rounded-lg bg-sky-950/80 border border-sky-800 text-sky-400">
              <Layers className="h-4 w-4" />
            </div>
            <DialogTitle>Create Kubernetes Namespace</DialogTitle>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {errorMessage && (
            <div className="p-3 bg-rose-950/80 border border-rose-800 rounded-lg text-rose-300 text-xs flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>{errorMessage}</span>
            </div>
          )}

          {/* Cluster Selection */}
          {availableClusters.length > 1 ? (
            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-zinc-300">Target Cluster</label>
              <select
                value={selectedCluster}
                onChange={(e) => setSelectedCluster(e.target.value)}
                className="w-full h-9 rounded-md border border-zinc-800 bg-zinc-950 px-3 py-1 text-xs text-zinc-200 focus:outline-none focus:ring-1 focus:ring-sky-500"
              >
                {availableClusters.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/80 text-xs">
              <span className="text-zinc-400">Target Cluster:</span>
              <span className="font-mono font-medium text-zinc-200">{selectedCluster || clusterId || availableClusters[0] || 'Default Cluster'}</span>
            </div>
          )}

          {/* Namespace Name */}
          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <label className="text-xs font-semibold text-zinc-300">
                Namespace Name <span className="text-rose-400">*</span>
              </label>
              {name.length > 0 && (
                <span className="text-[11px] flex items-center gap-1 font-mono">
                  {isNameValid ? (
                    <span className="text-emerald-400 flex items-center gap-1">
                      <CheckCircle2 className="h-3 w-3" /> Valid DNS-1123
                    </span>
                  ) : (
                    <span className="text-rose-400 flex items-center gap-1">
                      <AlertTriangle className="h-3 w-3" /> Invalid Name
                    </span>
                  )}
                </span>
              )}
            </div>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. apps-prod, databases, monitoring"
              className="bg-zinc-950 font-mono text-xs"
              autoFocus
            />
            <p className="text-[11px] text-zinc-500 leading-tight">
              Must be 1-63 lowercase alphanumeric characters or hyphens (<code>-</code>), starting and ending with an alphanumeric character.
            </p>
          </div>

          {/* Labels & Metadata Toggle */}
          <div className="pt-2 border-t border-zinc-800/80">
            <button
              type="button"
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="flex items-center gap-1.5 text-xs text-zinc-400 hover:text-zinc-200 transition-colors cursor-pointer"
            >
              <Tag className="h-3.5 w-3.5 text-sky-400" />
              <span>{showAdvanced ? 'Hide Metadata Labels' : 'Add Metadata Labels (Optional)'}</span>
            </button>

            {showAdvanced && (
              <div className="mt-3 space-y-2 p-3 rounded-xl bg-zinc-950/60 border border-zinc-800/80">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-medium text-zinc-400">Labels (Key/Value)</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleAddLabel}
                    className="h-6 px-2 text-[11px] text-sky-400 hover:text-sky-300 hover:bg-sky-950/40"
                  >
                    <Plus className="h-3 w-3 mr-1" />
                    Add Label
                  </Button>
                </div>

                {labels.length === 0 ? (
                  <p className="text-[11px] text-zinc-500 italic py-1">No custom labels configured.</p>
                ) : (
                  <div className="space-y-1.5">
                    {labels.map((item) => (
                      <div key={item.id} className="flex items-center gap-2">
                        <Input
                          placeholder="key (e.g. env)"
                          value={item.key}
                          onChange={(e) => handleLabelChange(item.id, 'key', e.target.value)}
                          className="h-7 text-xs font-mono bg-zinc-900"
                        />
                        <span className="text-zinc-500 text-xs">:</span>
                        <Input
                          placeholder="value (e.g. production)"
                          value={item.value}
                          onChange={(e) => handleLabelChange(item.id, 'value', e.target.value)}
                          className="h-7 text-xs font-mono bg-zinc-900"
                        />
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => handleRemoveLabel(item.id)}
                          className="h-7 w-7 p-0 text-zinc-500 hover:text-rose-400 hover:bg-rose-950/40 shrink-0"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleClose}
            disabled={createMutation.isPending}
          >
            Cancel
          </Button>

          <Button
            type="submit"
            size="sm"
            disabled={!isNameValid || createMutation.isPending}
            className="bg-sky-600 hover:bg-sky-500 text-white font-medium"
          >
            {createMutation.isPending ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin mr-1.5" />
                Creating Namespace...
              </>
            ) : (
              <>
                <Plus className="h-3.5 w-3.5 mr-1.5" />
                Create Namespace
              </>
            )}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

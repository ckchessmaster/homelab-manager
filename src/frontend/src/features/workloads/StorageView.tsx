import { useState, useEffect } from 'react'
import {
  HardDrive,
  CheckCircle2,
  RefreshCw,
  Search,
  Layers,
  Boxes,
} from 'lucide-react'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Select } from '../../components/ui/select'
import { useStorageOverview } from './useWorkloads'

interface StorageViewProps {
  clusterId: string
  selectedNamespace?: string
  onNamespaceChange?: (namespace: string) => void
  availableNamespaces?: string[]
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 GiB'
  const gib = bytes / (1024 * 1024 * 1024)
  if (gib >= 1024) {
    return `${(gib / 1024).toFixed(1)} TiB`
  }
  return `${gib.toFixed(1)} GiB`
}

export function StorageView({
  clusterId,
  selectedNamespace,
  onNamespaceChange,
  availableNamespaces = [],
}: StorageViewProps) {
  const [searchTerm, setSearchTerm] = useState('')
  const [localNamespace, setLocalNamespace] = useState(selectedNamespace || '')

  useEffect(() => {
    if (selectedNamespace !== undefined) {
      setLocalNamespace(selectedNamespace)
    }
  }, [selectedNamespace])

  const effectiveNamespace = localNamespace || undefined

  const {
    data: storage,
    isLoading,
    refetch,
    isFetching,
  } = useStorageOverview(clusterId, effectiveNamespace)

  const pvcs = Array.isArray(storage?.pvcs) ? storage.pvcs : []
  const storageClasses = Array.isArray(storage?.storageClasses) ? storage.storageClasses : []

  const filteredPvcs = pvcs.filter((p) => {
    if (!searchTerm.trim()) return true
    const q = searchTerm.toLowerCase()
    return (
      p.name.toLowerCase().includes(q) ||
      p.namespace.toLowerCase().includes(q) ||
      (p.storageClass && p.storageClass.toLowerCase().includes(q)) ||
      p.mountingPods.some((pod) => pod.toLowerCase().includes(q))
    )
  })

  return (
    <div className="space-y-6">
      {/* Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Total Provisioned Storage</span>
            <HardDrive className="h-4 w-4 text-amber-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {formatBytes(storage?.totalCapacityBytes || 0)}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Across all persistent volumes</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Bound Volumes</span>
            <CheckCircle2 className="h-4 w-4 text-emerald-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-emerald-400">
            {storage?.boundPvcs ?? 0} / {storage?.totalPvcs ?? 0}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">PVCs successfully attached</p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Storage Driver (CSI)</span>
            <span className="h-2 w-2 rounded-full bg-emerald-400 inline-block" />
          </div>
          <div className="mt-2 text-base font-bold text-zinc-100">
            {storage?.longhornDetected ? 'Longhorn Distributed' : 'Kubernetes CSI'}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">
            {storage?.longhornDetected ? 'Replicated block storage active' : 'Local / standard volume driver'}
          </p>
        </div>

        <div className="p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-zinc-400">Storage Classes</span>
            <Layers className="h-4 w-4 text-purple-400" />
          </div>
          <div className="mt-2 text-2xl font-bold text-zinc-100">
            {storageClasses.length}
          </div>
          <p className="text-[11px] text-zinc-500 mt-0.5">Available provisioning profiles</p>
        </div>
      </div>

      {/* StorageClasses Badges */}
      {storageClasses.length > 0 && (
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-xs font-semibold text-zinc-400">Storage Classes:</span>
          {storageClasses.map((sc) => (
            <div
              key={sc.name}
              className="flex items-center gap-1.5 px-3 py-1 rounded-lg bg-zinc-900 border border-zinc-800 text-xs font-mono"
            >
              <span className="font-bold text-zinc-200">{sc.name}</span>
              <span className="text-zinc-500 text-[10px]">({sc.provisioner.split('/').pop()})</span>
              {sc.isDefault && (
                <span className="text-[9px] uppercase px-1.5 py-0.2 rounded bg-amber-950 border border-amber-800 text-amber-300 font-sans font-semibold">
                  Default
                </span>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Search & Refresh Toolbar */}
      <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
        <div className="flex items-center gap-3 flex-1 max-w-xl">
          <div className="relative min-w-[220px] flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
            <Input
              placeholder="Search PVC name, storage class, or mounting pod..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-9 bg-zinc-950/80 text-xs"
            />
          </div>

          {availableNamespaces.length > 0 && (
            <div className="w-40 shrink-0">
              <Select
                value={localNamespace}
                onChange={(e) => {
                  const val = e.target.value
                  setLocalNamespace(val)
                  onNamespaceChange?.(val)
                }}
                className="bg-zinc-950/80 text-xs h-9"
              >
                <option value="">All Namespaces</option>
                {availableNamespaces.map((ns) => (
                  <option key={ns} value={ns}>
                    {ns}
                  </option>
                ))}
              </Select>
            </div>
          )}
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => refetch()}
          disabled={isFetching}
          className="gap-1.5 text-xs shrink-0"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </div>

      {/* PVC Inventory Table */}
      <div className="space-y-3">
        <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
          <HardDrive className="h-4 w-4 text-amber-400" />
          PersistentVolumeClaim (PVC) Inventory
        </h3>

        {isLoading ? (
          <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/30 text-xs text-zinc-400">
            <RefreshCw className="h-6 w-6 animate-spin mx-auto text-amber-400 mb-2" />
            Querying storage volumes and Longhorn bindings...
          </div>
        ) : filteredPvcs.length === 0 ? (
          <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/40 text-xs text-zinc-400 space-y-1">
            <HardDrive className="h-8 w-8 mx-auto text-zinc-600 mb-2" />
            <div className="font-semibold text-zinc-200">No PersistentVolumeClaims found</div>
            <div>Persistent volumes created by stateful apps or Longhorn will appear here.</div>
          </div>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60">
            <table className="w-full text-left text-xs text-zinc-300">
              <thead className="bg-zinc-950/80 text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800">
                <tr>
                  <th className="p-3">PVC Name</th>
                  <th className="p-3">Namespace</th>
                  <th className="p-3">Status</th>
                  <th className="p-3">Replica Health</th>
                  <th className="p-3">Capacity</th>
                  <th className="p-3">Usage</th>
                  <th className="p-3">Storage Class</th>
                  <th className="p-3">Access Mode</th>
                  <th className="p-3">Mounting Workload / Pod</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60 font-mono">
                {filteredPvcs.map((pvc) => {
                  const hasUsage = pvc.usedBytes !== undefined && pvc.usedBytes !== null && Boolean(pvc.capacityBytes)
                  const usagePercent = hasUsage && pvc.capacityBytes
                    ? Math.min(100, Math.round((pvc.usedBytes! / pvc.capacityBytes) * 100))
                    : 0
                  const replicaHealth = pvc.replicaHealth || (pvc.status === 'Bound' ? 'Healthy' : undefined)

                  return (
                    <tr key={`${pvc.namespace}-${pvc.name}`} className="hover:bg-zinc-800/40 transition-colors">
                      <td className="p-3 font-semibold text-zinc-100 flex items-center gap-1.5">
                        <HardDrive className="h-3.5 w-3.5 text-amber-400 shrink-0" />
                        <span>{pvc.name}</span>
                      </td>
                      <td className="p-3 text-amber-300 font-sans">{pvc.namespace}</td>
                      <td className="p-3 font-sans">
                        <Badge
                          variant={pvc.status === 'Bound' ? 'success' : 'warning'}
                          dot
                          className="text-[10px]"
                        >
                          {pvc.status}
                        </Badge>
                      </td>
                      <td className="p-3">
                        {replicaHealth ? (
                          <span
                            className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[10px] font-sans font-medium border ${
                              replicaHealth === 'Healthy'
                                ? 'bg-emerald-950/80 text-emerald-300 border-emerald-800/80'
                                : replicaHealth === 'Degraded'
                                ? 'bg-amber-950/80 text-amber-300 border-amber-800/80'
                                : 'bg-rose-950/80 text-rose-300 border-rose-800/80'
                            }`}
                          >
                            <span
                              className={`h-1.5 w-1.5 rounded-full ${
                                replicaHealth === 'Healthy'
                                  ? 'bg-emerald-400'
                                  : replicaHealth === 'Degraded'
                                  ? 'bg-amber-400'
                                  : 'bg-rose-400'
                              }`}
                            />
                            <span>{replicaHealth}</span>
                          </span>
                        ) : (
                          <span className="text-zinc-500 font-mono text-[11px]">-</span>
                        )}
                      </td>
                      <td className="p-3 text-zinc-200 font-bold">{pvc.capacity || 'Unknown'}</td>
                      <td className="p-3">
                        {hasUsage && pvc.capacityBytes ? (
                          <div className="space-y-1 w-28">
                            <div className="flex items-center justify-between text-[10px] font-mono">
                              <span className="text-zinc-300 font-semibold">
                                {formatBytes(pvc.usedBytes!)}
                              </span>
                              <span className="text-zinc-500">
                                {usagePercent}%
                              </span>
                            </div>
                            <div className="w-full h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                              <div
                                className={`h-full rounded-full transition-all ${
                                  usagePercent > 90
                                    ? 'bg-rose-500'
                                    : usagePercent > 75
                                    ? 'bg-amber-400'
                                    : 'bg-sky-500'
                                }`}
                                style={{
                                  width: `${Math.min(100, Math.max(3, usagePercent))}%`,
                                }}
                              />
                            </div>
                          </div>
                        ) : (
                          <span className="text-zinc-600 font-mono text-[11px]">-</span>
                        )}
                      </td>
                      <td className="p-3 text-sky-400">{pvc.storageClass || 'default'}</td>
                      <td className="p-3 text-zinc-400">
                        {pvc.accessModes.join(', ') || 'RWO'}
                      </td>
                      <td className="p-3">
                        {pvc.mountingPods && pvc.mountingPods.length > 0 ? (
                          <div className="flex flex-wrap gap-1">
                            {pvc.mountingPods.map((pod) => (
                              <span
                                key={pod}
                                className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-zinc-950 border border-zinc-800 text-emerald-400 text-[10px]"
                              >
                                <Boxes className="h-2.5 w-2.5" />
                                {pod}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <span className="text-zinc-600">Unmounted</span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}

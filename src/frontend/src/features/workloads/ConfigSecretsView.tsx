import { useState, useMemo } from 'react'
import {
  KeyRound,
  FileText,
  FileCode,
  Search,
  RefreshCw,
  Plus,
  Trash2,
  Eye,
  EyeOff,
  Copy,
  Check,
  AlertTriangle,
  Loader2,
  Link2,
  Edit,
} from 'lucide-react'
import {
  isConnectionString,
  hasPercentEncoding,
  autoDecodeSecretForDisplay,
} from '../../utils/urlEncoding'
import { EditSecretModal, EditConfigMapModal } from './EditResourceModal'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import {
  useSecrets,
  useSecretDetail,
  useDeleteSecret,
  useConfigMaps,
  useConfigMapDetail,
  useDeleteConfigMap,
} from './useKubernetesResources'
import type { K8sSecretSummary, K8sConfigMapSummary } from '../../api/kubernetesResources'
import { useAuthUser } from '../auth/useAuthUser'

interface ConfigSecretsViewProps {
  clusterId: string
  selectedNamespace?: string
  availableNamespaces?: string[]
  onOpenCreateResource?: (tab?: 'secret' | 'configmap' | 'yaml') => void
}

export function ConfigSecretsView({
  clusterId,
  selectedNamespace,
  onOpenCreateResource,
}: ConfigSecretsViewProps) {
  const [subType, setSubType] = useState<'secrets' | 'configmaps'>('secrets')
  const [searchTerm, setSearchTerm] = useState('')
  const [hideSystem, setHideSystem] = useState(true)

  // Inspection, Editing & Deletion Modals
  const [inspectSecret, setInspectSecret] = useState<K8sSecretSummary | null>(null)
  const [inspectConfigMap, setInspectConfigMap] = useState<K8sConfigMapSummary | null>(null)
  const [editingSecret, setEditingSecret] = useState<K8sSecretSummary | null>(null)
  const [editingConfigMap, setEditingConfigMap] = useState<K8sConfigMapSummary | null>(null)
  const [deletingSecret, setDeletingSecret] = useState<K8sSecretSummary | null>(null)
  const [deletingConfigMap, setDeletingConfigMap] = useState<K8sConfigMapSummary | null>(null)

  const { isOperator } = useAuthUser()

  const {
    data: secrets = [],
    isLoading: isLoadingSecrets,
    refetch: refetchSecrets,
    isFetching: isFetchingSecrets,
  } = useSecrets(clusterId, selectedNamespace)

  const {
    data: configMaps = [],
    isLoading: isLoadingConfigMaps,
    refetch: refetchConfigMaps,
    isFetching: isFetchingConfigMaps,
  } = useConfigMaps(clusterId, selectedNamespace)

  const deleteSecretMutation = useDeleteSecret(clusterId)
  const deleteConfigMapMutation = useDeleteConfigMap(clusterId)

  // Filtered Secrets
  const filteredSecrets = useMemo(() => {
    return secrets.filter((s) => {
      if (hideSystem && s.isSystem) return false
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase()
        const matchesName = s.name.toLowerCase().includes(q)
        const matchesNs = s.namespace.toLowerCase().includes(q)
        const matchesKey = s.keys.some((k) => k.toLowerCase().includes(q))
        if (!matchesName && !matchesNs && !matchesKey) return false
      }
      return true
    })
  }, [secrets, hideSystem, searchTerm])

  // Filtered ConfigMaps
  const filteredConfigMaps = useMemo(() => {
    return configMaps.filter((c) => {
      if (hideSystem && c.isSystem) return false
      if (searchTerm.trim()) {
        const q = searchTerm.toLowerCase()
        const matchesName = c.name.toLowerCase().includes(q)
        const matchesNs = c.namespace.toLowerCase().includes(q)
        const matchesKey = c.keys.some((k) => k.toLowerCase().includes(q))
        if (!matchesName && !matchesNs && !matchesKey) return false
      }
      return true
    })
  }, [configMaps, hideSystem, searchTerm])

  const handleDeleteSecret = async () => {
    if (!deletingSecret) return
    try {
      await deleteSecretMutation.mutateAsync({
        namespaceName: deletingSecret.namespace,
        name: deletingSecret.name,
      })
      setDeletingSecret(null)
    } catch {
      // Handled by react query
    }
  }

  const handleDeleteConfigMap = async () => {
    if (!deletingConfigMap) return
    try {
      await deleteConfigMapMutation.mutateAsync({
        namespaceName: deletingConfigMap.namespace,
        name: deletingConfigMap.name,
      })
      setDeletingConfigMap(null)
    } catch {
      // Handled by react query
    }
  }

  const formatAge = (dateStr?: string | null) => {
    if (!dateStr) return 'N/A'
    const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
    if (diff < 60) return `${diff}s`
    if (diff < 3600) return `${Math.floor(diff / 60)}m`
    if (diff < 86400) return `${Math.floor(diff / 3600)}h`
    return `${Math.floor(diff / 86400)}d`
  }

  return (
    <div className="space-y-4">
      {/* Top Filter & Actions Header */}
      <div className="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-4 p-4 rounded-xl bg-zinc-900/60 border border-zinc-800/80">
        {/* Toggle between Secrets and ConfigMaps */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setSubType('secrets')}
            className={`flex items-center gap-2 px-3.5 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
              subType === 'secrets'
                ? 'bg-purple-600 text-white shadow-sm'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <KeyRound className="h-4 w-4" />
            <span>Secrets</span>
            <span className="ml-1 px-1.5 py-0.2 rounded-full text-[10px] bg-purple-950 text-purple-200 border border-purple-800">
              {filteredSecrets.length}
            </span>
          </button>

          <button
            type="button"
            onClick={() => setSubType('configmaps')}
            className={`flex items-center gap-2 px-3.5 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
              subType === 'configmaps'
                ? 'bg-sky-600 text-white shadow-sm'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/60'
            }`}
          >
            <FileText className="h-4 w-4" />
            <span>ConfigMaps</span>
            <span className="ml-1 px-1.5 py-0.2 rounded-full text-[10px] bg-sky-950 text-sky-200 border border-sky-800">
              {filteredConfigMaps.length}
            </span>
          </button>
        </div>

        {/* Search, System Filter & New Resource Buttons */}
        <div className="flex flex-wrap items-center gap-2.5">
          <div className="relative min-w-[220px]">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
            <Input
              placeholder={`Search ${subType}...`}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-8 text-xs bg-zinc-950/60 border-zinc-700 h-8"
            />
          </div>

          <button
            type="button"
            onClick={() => setHideSystem(!hideSystem)}
            className={`px-2.5 py-1.5 rounded-lg text-xs border transition-colors cursor-pointer ${
              hideSystem
                ? 'bg-zinc-900 border-zinc-700 text-zinc-300'
                : 'bg-purple-950/40 border-purple-800 text-purple-300 font-semibold'
            }`}
            title="Toggle display of kube-system resources"
          >
            {hideSystem ? 'Hide System' : 'Showing System'}
          </button>

          <Button
            variant="ghost"
            size="sm"
            onClick={() => (subType === 'secrets' ? refetchSecrets() : refetchConfigMaps())}
            disabled={isFetchingSecrets || isFetchingConfigMaps}
            className="h-8 px-2 text-zinc-400 hover:text-white"
            title="Refresh"
          >
            <RefreshCw
              className={`h-3.5 w-3.5 ${isFetchingSecrets || isFetchingConfigMaps ? 'animate-spin' : ''}`}
            />
          </Button>

          {isOperator && (
            <div className="flex items-center gap-1.5">
              <Button
                size="sm"
                onClick={() => onOpenCreateResource?.(subType === 'secrets' ? 'secret' : 'configmap')}
                className={`h-8 text-xs gap-1.5 font-semibold ${
                  subType === 'secrets'
                    ? 'bg-purple-600 hover:bg-purple-500 text-white'
                    : 'bg-sky-600 hover:bg-sky-500 text-white'
                }`}
              >
                <Plus className="h-3.5 w-3.5" />
                <span>{subType === 'secrets' ? 'New Secret' : 'New ConfigMap'}</span>
              </Button>

              <Button
                size="sm"
                variant="outline"
                onClick={() => onOpenCreateResource?.('yaml')}
                className="h-8 text-xs border-zinc-700 text-zinc-300 hover:text-white gap-1"
                title="Apply raw Kubernetes YAML manifest"
              >
                <FileCode className="h-3.5 w-3.5 text-emerald-400" />
                <span>Raw Manifest</span>
              </Button>
            </div>
          )}
        </div>
      </div>

      {/* SECRETS TABLE */}
      {subType === 'secrets' && (
        <div className="rounded-xl border border-zinc-800/80 bg-zinc-950/60 overflow-hidden">
          {isLoadingSecrets ? (
            <div className="p-12 text-center text-xs text-zinc-500 flex items-center justify-center gap-2">
              <Loader2 className="h-4 w-4 animate-spin text-purple-400" />
              <span>Querying Kubernetes Secrets...</span>
            </div>
          ) : filteredSecrets.length === 0 ? (
            <div className="p-12 text-center space-y-2">
              <KeyRound className="h-8 w-8 text-zinc-600 mx-auto" />
              <p className="text-sm font-semibold text-zinc-300">No Secrets Found</p>
              <p className="text-xs text-zinc-500 max-w-sm mx-auto">
                No secrets matching your filter in {selectedNamespace || 'the active cluster'}.
              </p>
              {isOperator && (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => onOpenCreateResource?.('secret')}
                  className="mt-2 text-xs border-purple-700/60 text-purple-300 gap-1.5"
                >
                  <Plus className="h-3 w-3" />
                  Create Secret
                </Button>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs border-collapse">
                <thead>
                  <tr className="border-b border-zinc-800/80 bg-zinc-900/50 text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    <th className="py-3 px-4">Name</th>
                    <th className="py-3 px-4">Namespace</th>
                    <th className="py-3 px-4">Type</th>
                    <th className="py-3 px-4">Keys / Data</th>
                    <th className="py-3 px-4">Age</th>
                    <th className="py-3 px-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60">
                  {filteredSecrets.map((s) => (
                    <tr
                      key={`${s.namespace}/${s.name}`}
                      className="hover:bg-zinc-900/40 transition-colors group"
                    >
                      <td className="py-3 px-4 font-mono font-semibold text-zinc-100 flex items-center gap-2">
                        <KeyRound className="h-3.5 w-3.5 text-purple-400 shrink-0" />
                        <span className="truncate">{s.name}</span>
                        {s.isSystem && (
                          <Badge variant="outline" className="text-[9px] py-0 border-zinc-700 text-zinc-500">
                            system
                          </Badge>
                        )}
                      </td>
                      <td className="py-3 px-4 font-mono text-zinc-300">{s.namespace}</td>
                      <td className="py-3 px-4">
                        <Badge
                          variant="outline"
                          className={`text-[10px] font-mono py-0 ${
                            s.type === 'kubernetes.io/tls'
                              ? 'border-emerald-500/40 text-emerald-300 bg-emerald-950/20'
                              : s.type === 'kubernetes.io/dockerconfigjson'
                              ? 'border-sky-500/40 text-sky-300 bg-sky-950/20'
                              : 'border-zinc-700 text-zinc-400'
                          }`}
                        >
                          {s.type.replace('kubernetes.io/', '')}
                        </Badge>
                      </td>
                      <td className="py-3 px-4">
                        <div className="flex flex-wrap items-center gap-1 max-w-md">
                          <span className="font-semibold text-zinc-300 mr-1">
                            {s.keysCount} {s.keysCount === 1 ? 'key' : 'keys'}
                          </span>
                          {s.keys.slice(0, 3).map((k) => (
                            <span
                              key={k}
                              className="px-1.5 py-0.2 rounded bg-zinc-900 border border-zinc-800 font-mono text-[10px] text-zinc-400"
                            >
                              {k}
                            </span>
                          ))}
                          {s.keys.length > 3 && (
                            <span className="text-[10px] text-zinc-500">
                              +{s.keys.length - 3} more
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3 px-4 text-zinc-400 font-mono text-[11px]">
                        {formatAge(s.creationTimestamp)}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            type="button"
                            onClick={() => setInspectSecret(s)}
                            className="px-2 py-1 rounded text-[11px] font-medium text-purple-300 hover:text-white bg-purple-950/30 hover:bg-purple-900/50 border border-purple-800/40 transition-colors cursor-pointer"
                          >
                            Inspect
                          </button>
                          {isOperator && !s.isSystem && (
                            <>
                              <button
                                type="button"
                                onClick={() => setEditingSecret(s)}
                                className="px-2 py-1 rounded text-[11px] font-medium text-amber-300 hover:text-white bg-amber-950/30 hover:bg-amber-900/50 border border-amber-800/40 transition-colors cursor-pointer flex items-center gap-1"
                                title="Edit secret data"
                              >
                                <Edit className="h-3 w-3" />
                                <span>Edit</span>
                              </button>
                              <button
                                type="button"
                                onClick={() => setDeletingSecret(s)}
                                className="p-1 rounded text-zinc-500 hover:text-rose-400 hover:bg-rose-950/30 transition-colors cursor-pointer"
                                title="Delete secret"
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* CONFIGMAPS TABLE */}
      {subType === 'configmaps' && (
        <div className="rounded-xl border border-zinc-800/80 bg-zinc-950/60 overflow-hidden">
          {isLoadingConfigMaps ? (
            <div className="p-12 text-center text-xs text-zinc-500 flex items-center justify-center gap-2">
              <Loader2 className="h-4 w-4 animate-spin text-sky-400" />
              <span>Querying Kubernetes ConfigMaps...</span>
            </div>
          ) : filteredConfigMaps.length === 0 ? (
            <div className="p-12 text-center space-y-2">
              <FileText className="h-8 w-8 text-zinc-600 mx-auto" />
              <p className="text-sm font-semibold text-zinc-300">No ConfigMaps Found</p>
              <p className="text-xs text-zinc-500 max-w-sm mx-auto">
                No config maps matching your filter in {selectedNamespace || 'the active cluster'}.
              </p>
              {isOperator && (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => onOpenCreateResource?.('configmap')}
                  className="mt-2 text-xs border-sky-700/60 text-sky-300 gap-1.5"
                >
                  <Plus className="h-3 w-3" />
                  Create ConfigMap
                </Button>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs border-collapse">
                <thead>
                  <tr className="border-b border-zinc-800/80 bg-zinc-900/50 text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    <th className="py-3 px-4">Name</th>
                    <th className="py-3 px-4">Namespace</th>
                    <th className="py-3 px-4">Keys / Data Entries</th>
                    <th className="py-3 px-4">Age</th>
                    <th className="py-3 px-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60">
                  {filteredConfigMaps.map((c) => (
                    <tr
                      key={`${c.namespace}/${c.name}`}
                      className="hover:bg-zinc-900/40 transition-colors group"
                    >
                      <td className="py-3 px-4 font-mono font-semibold text-zinc-100 flex items-center gap-2">
                        <FileText className="h-3.5 w-3.5 text-sky-400 shrink-0" />
                        <span className="truncate">{c.name}</span>
                        {c.isSystem && (
                          <Badge variant="outline" className="text-[9px] py-0 border-zinc-700 text-zinc-500">
                            system
                          </Badge>
                        )}
                      </td>
                      <td className="py-3 px-4 font-mono text-zinc-300">{c.namespace}</td>
                      <td className="py-3 px-4">
                        <div className="flex flex-wrap items-center gap-1 max-w-md">
                          <span className="font-semibold text-zinc-300 mr-1">
                            {c.keysCount} {c.keysCount === 1 ? 'entry' : 'entries'}
                          </span>
                          {c.keys.slice(0, 3).map((k) => (
                            <span
                              key={k}
                              className="px-1.5 py-0.2 rounded bg-zinc-900 border border-zinc-800 font-mono text-[10px] text-zinc-400"
                            >
                              {k}
                            </span>
                          ))}
                          {c.keys.length > 3 && (
                            <span className="text-[10px] text-zinc-500">
                              +{c.keys.length - 3} more
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3 px-4 text-zinc-400 font-mono text-[11px]">
                        {formatAge(c.creationTimestamp)}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            type="button"
                            onClick={() => setInspectConfigMap(c)}
                            className="px-2 py-1 rounded text-[11px] font-medium text-sky-300 hover:text-white bg-sky-950/30 hover:bg-sky-900/50 border border-sky-800/40 transition-colors cursor-pointer"
                          >
                            Inspect
                          </button>
                          {isOperator && !c.isSystem && (
                            <>
                              <button
                                type="button"
                                onClick={() => setEditingConfigMap(c)}
                                className="px-2 py-1 rounded text-[11px] font-medium text-amber-300 hover:text-white bg-amber-950/30 hover:bg-amber-900/50 border border-amber-800/40 transition-colors cursor-pointer flex items-center gap-1"
                                title="Edit config map data"
                              >
                                <Edit className="h-3 w-3" />
                                <span>Edit</span>
                              </button>
                              <button
                                type="button"
                                onClick={() => setDeletingConfigMap(c)}
                                className="p-1 rounded text-zinc-500 hover:text-rose-400 hover:bg-rose-950/30 transition-colors cursor-pointer"
                                title="Delete config map"
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* INSPECT SECRET MODAL */}
      {inspectSecret && (
        <SecretDetailModal
          open={Boolean(inspectSecret)}
          onClose={() => setInspectSecret(null)}
          clusterId={clusterId}
          namespace={inspectSecret.namespace}
          name={inspectSecret.name}
          onEdit={() => setEditingSecret(inspectSecret)}
        />
      )}

      {/* INSPECT CONFIGMAP MODAL */}
      {inspectConfigMap && (
        <ConfigMapDetailModal
          open={Boolean(inspectConfigMap)}
          onClose={() => setInspectConfigMap(null)}
          clusterId={clusterId}
          namespace={inspectConfigMap.namespace}
          name={inspectConfigMap.name}
          onEdit={() => setEditingConfigMap(inspectConfigMap)}
        />
      )}

      {/* EDIT SECRET MODAL */}
      {editingSecret && (
        <EditSecretModal
          open={Boolean(editingSecret)}
          onClose={() => setEditingSecret(null)}
          clusterId={clusterId}
          namespace={editingSecret.namespace}
          name={editingSecret.name}
        />
      )}

      {/* EDIT CONFIGMAP MODAL */}
      {editingConfigMap && (
        <EditConfigMapModal
          open={Boolean(editingConfigMap)}
          onClose={() => setEditingConfigMap(null)}
          clusterId={clusterId}
          namespace={editingConfigMap.namespace}
          name={editingConfigMap.name}
        />
      )}

      {/* DELETE SECRET CONFIRMATION */}
      {deletingSecret && (
        <Dialog open={true} onClose={() => setDeletingSecret(null)} maxWidth="md">
          <DialogHeader onClose={() => setDeletingSecret(null)}>
            <div className="flex items-center gap-2 text-rose-400">
              <AlertTriangle className="h-5 w-5 shrink-0" />
              <DialogTitle className="text-zinc-100">Delete Kubernetes Secret</DialogTitle>
            </div>
          </DialogHeader>
          <DialogBody className="space-y-3">
            <p className="text-sm text-zinc-300">
              Are you sure you want to delete secret{' '}
              <strong className="text-white font-mono">{deletingSecret.name}</strong> in namespace{' '}
              <strong className="text-white font-mono">{deletingSecret.namespace}</strong>?
            </p>
            <div className="p-3 bg-rose-950/30 border border-rose-800/40 rounded-lg text-xs text-rose-200/90 leading-relaxed">
              Any running pods or workloads referencing this secret may experience crash loops or authentication failures.
            </div>
          </DialogBody>
          <DialogFooter>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setDeletingSecret(null)}
              disabled={deleteSecretMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleDeleteSecret}
              disabled={deleteSecretMutation.isPending}
              className="bg-rose-600 hover:bg-rose-500 text-white font-semibold gap-1.5"
            >
              {deleteSecretMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="h-4 w-4" />
              )}
              Delete Secret
            </Button>
          </DialogFooter>
        </Dialog>
      )}

      {/* DELETE CONFIGMAP CONFIRMATION */}
      {deletingConfigMap && (
        <Dialog open={true} onClose={() => setDeletingConfigMap(null)} maxWidth="md">
          <DialogHeader onClose={() => setDeletingConfigMap(null)}>
            <div className="flex items-center gap-2 text-rose-400">
              <AlertTriangle className="h-5 w-5 shrink-0" />
              <DialogTitle className="text-zinc-100">Delete ConfigMap</DialogTitle>
            </div>
          </DialogHeader>
          <DialogBody className="space-y-3">
            <p className="text-sm text-zinc-300">
              Are you sure you want to delete config map{' '}
              <strong className="text-white font-mono">{deletingConfigMap.name}</strong> in namespace{' '}
              <strong className="text-white font-mono">{deletingConfigMap.namespace}</strong>?
            </p>
          </DialogBody>
          <DialogFooter>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setDeletingConfigMap(null)}
              disabled={deleteConfigMapMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleDeleteConfigMap}
              disabled={deleteConfigMapMutation.isPending}
              className="bg-rose-600 hover:bg-rose-500 text-white font-semibold gap-1.5"
            >
              {deleteConfigMapMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="h-4 w-4" />
              )}
              Delete ConfigMap
            </Button>
          </DialogFooter>
        </Dialog>
      )}
    </div>
  )
}

function SecretDetailModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onEdit,
}: {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onEdit?: () => void
}) {
  const { isOperator } = useAuthUser()
  const [reveal, setReveal] = useState(false)
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const [rowDecodeMode, setRowDecodeMode] = useState<Record<string, 'decoded' | 'raw'>>({})
  const { data: secret, isLoading } = useSecretDetail(clusterId, namespace, name, reveal)

  const handleCopy = (val: string, keyName: string) => {
    navigator.clipboard.writeText(val)
    setCopiedKey(keyName)
    setTimeout(() => setCopiedKey(null), 2000)
  }

  const hasAnyEncoded = useMemo(() => {
    if (!reveal || !secret?.data) return false
    return Object.values(secret.data).some(
      v => isConnectionString(v) || hasPercentEncoding(v)
    )
  }, [reveal, secret?.data])

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2 text-purple-400">
          <KeyRound className="h-5 w-5 shrink-0" />
          <DialogTitle className="text-zinc-100 font-mono">{name}</DialogTitle>
          <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
            {namespace}
          </Badge>
        </div>
      </DialogHeader>
      <DialogBody className="space-y-4">
        {isLoading ? (
          <div className="p-8 text-center text-xs text-zinc-500 flex items-center justify-center gap-2">
            <Loader2 className="h-4 w-4 animate-spin text-purple-400" />
            <span>Loading secret data...</span>
          </div>
        ) : !secret ? (
          <p className="text-xs text-zinc-400">Secret not found.</p>
        ) : (
          <div className="space-y-4">
            <div className="flex items-center justify-between p-3 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs">
              <div className="space-y-0.5">
                <span className="text-zinc-500 text-[10px] uppercase tracking-wider">Type</span>
                <p className="font-mono text-zinc-200">{secret.type}</p>
              </div>
              <button
                type="button"
                onClick={() => setReveal(!reveal)}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-purple-950/40 text-purple-300 border border-purple-800 hover:bg-purple-900/60 transition-colors cursor-pointer"
              >
                {reveal ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                <span>{reveal ? 'Mask Values' : 'Reveal Values'}</span>
              </button>
            </div>

            {/* Keys and Values */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                  Keys & Values
                </span>
                {hasAnyEncoded && (
                  <span className="text-[10px] text-purple-400 flex items-center gap-1">
                    <Link2 className="h-3 w-3" />
                    URL-encoded connection strings detected
                  </span>
                )}
              </div>
              <div className="space-y-2 max-h-72 overflow-y-auto pr-1">
                {Object.entries(secret.data || {}).map(([key, val]) => {
                  const isConn = isConnectionString(val)
                  const decodedVal = autoDecodeSecretForDisplay(val)
                  const hasDiff = reveal && decodedVal !== val
                  const currentMode = rowDecodeMode[key] ?? 'decoded'
                  const displayVal = reveal && currentMode === 'decoded' ? decodedVal : val

                  return (
                    <div
                      key={key}
                      className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 space-y-1.5"
                    >
                      <div className="flex items-center justify-between gap-2 flex-wrap">
                        <div className="flex items-center gap-1.5">
                          <span className="font-mono font-semibold text-xs text-purple-300">{key}</span>
                          {isConn && (
                            <Badge variant="outline" className="text-[9px] bg-sky-950/50 text-sky-300 border-sky-800/60 font-mono py-0">
                              DSN
                            </Badge>
                          )}
                          {hasDiff && (
                            <Badge variant="outline" className="text-[9px] bg-purple-950/50 text-purple-300 border-purple-800/60 font-mono py-0">
                              Encoded
                            </Badge>
                          )}
                        </div>

                        <div className="flex items-center gap-2">
                          {hasDiff && (
                            <div className="flex items-center bg-zinc-900 border border-zinc-800 rounded p-0.5 text-[10px]">
                              <button
                                type="button"
                                onClick={() => setRowDecodeMode(prev => ({ ...prev, [key]: 'decoded' }))}
                                className={`px-2 py-0.5 rounded cursor-pointer transition-colors ${
                                  currentMode === 'decoded'
                                    ? 'bg-purple-900/60 text-purple-200 font-medium'
                                    : 'text-zinc-400 hover:text-zinc-200'
                                }`}
                              >
                                Decoded Plaintext
                              </button>
                              <button
                                type="button"
                                onClick={() => setRowDecodeMode(prev => ({ ...prev, [key]: 'raw' }))}
                                className={`px-2 py-0.5 rounded cursor-pointer transition-colors ${
                                  currentMode === 'raw'
                                    ? 'bg-purple-900/60 text-purple-200 font-medium'
                                    : 'text-zinc-400 hover:text-zinc-200'
                                }`}
                              >
                                Raw in K8s
                              </button>
                            </div>
                          )}

                          {hasDiff ? (
                            <div className="flex items-center gap-1">
                              <button
                                type="button"
                                onClick={() => handleCopy(decodedVal, `${key}-dec`)}
                                className="text-zinc-400 hover:text-purple-300 px-1.5 py-0.5 rounded text-[10px] flex items-center gap-1 border border-zinc-800 hover:border-purple-800 bg-zinc-900 cursor-pointer"
                                title="Copy decoded plaintext password"
                              >
                                {copiedKey === `${key}-dec` ? (
                                  <>
                                    <Check className="h-3 w-3 text-emerald-400" />
                                    <span className="text-emerald-400 font-medium">Copied Decoded</span>
                                  </>
                                ) : (
                                  <>
                                    <Copy className="h-3 w-3" />
                                    <span>Copy Decoded</span>
                                  </>
                                )}
                              </button>
                              <button
                                type="button"
                                onClick={() => handleCopy(val, `${key}-raw`)}
                                className="text-zinc-400 hover:text-purple-300 px-1.5 py-0.5 rounded text-[10px] flex items-center gap-1 border border-zinc-800 hover:border-purple-800 bg-zinc-900 cursor-pointer"
                                title="Copy raw encoded value as stored in Kubernetes Secret"
                              >
                                {copiedKey === `${key}-raw` ? (
                                  <>
                                    <Check className="h-3 w-3 text-emerald-400" />
                                    <span className="text-emerald-400 font-medium">Copied Raw</span>
                                  </>
                                ) : (
                                  <>
                                    <Copy className="h-3 w-3" />
                                    <span>Copy Raw</span>
                                  </>
                                )}
                              </button>
                            </div>
                          ) : (
                            <button
                              type="button"
                              onClick={() => handleCopy(displayVal, key)}
                              className="text-zinc-500 hover:text-zinc-200 p-1 text-[11px] flex items-center gap-1 cursor-pointer"
                              title="Copy value"
                            >
                              {copiedKey === key ? (
                                <>
                                  <Check className="h-3 w-3 text-emerald-400" />
                                  <span className="text-emerald-400 text-[10px]">Copied</span>
                                </>
                              ) : (
                                <>
                                  <Copy className="h-3 w-3" />
                                  <span className="text-[10px]">Copy</span>
                                </>
                              )}
                            </button>
                          )}
                        </div>
                      </div>
                      <pre className="font-mono text-xs text-zinc-200 bg-zinc-900/70 p-2 rounded border border-zinc-800/80 overflow-x-auto whitespace-pre-wrap break-all">
                        {displayVal}
                      </pre>
                      {hasDiff && currentMode === 'decoded' && (
                        <div className="text-[10px] text-zinc-500 flex items-center gap-1 pt-0.5">
                          <Link2 className="h-2.5 w-2.5 text-purple-400" />
                          <span>Showing decoded plaintext credentials. Toggle to &quot;Raw in K8s&quot; to inspect percent-encoded string.</span>
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          </div>
        )}
      </DialogBody>
      <DialogFooter className="flex items-center justify-between">
        {isOperator && onEdit && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              onClose()
              onEdit()
            }}
            className="border-amber-800 text-amber-300 hover:bg-amber-950/50 hover:text-white gap-1.5"
          >
            <Edit className="h-3.5 w-3.5" />
            Edit Secret
          </Button>
        )}
        <Button variant="secondary" size="sm" onClick={onClose} className="ml-auto">
          Close
        </Button>
      </DialogFooter>
    </Dialog>
  )
}

function ConfigMapDetailModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onEdit,
}: {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onEdit?: () => void
}) {
  const { isOperator } = useAuthUser()
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const { data: configMap, isLoading } = useConfigMapDetail(clusterId, namespace, name)

  const handleCopy = (val: string, keyName: string) => {
    navigator.clipboard.writeText(val)
    setCopiedKey(keyName)
    setTimeout(() => setCopiedKey(null), 2000)
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2 text-sky-400">
          <FileText className="h-5 w-5 shrink-0" />
          <DialogTitle className="text-zinc-100 font-mono">{name}</DialogTitle>
          <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
            {namespace}
          </Badge>
        </div>
      </DialogHeader>
      <DialogBody className="space-y-4">
        {isLoading ? (
          <div className="p-8 text-center text-xs text-zinc-500 flex items-center justify-center gap-2">
            <Loader2 className="h-4 w-4 animate-spin text-sky-400" />
            <span>Loading config map data...</span>
          </div>
        ) : !configMap ? (
          <p className="text-xs text-zinc-400">ConfigMap not found.</p>
        ) : (
          <div className="space-y-3">
            <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
              Data Entries ({Object.keys(configMap.data || {}).length})
            </span>
            <div className="space-y-2 max-h-80 overflow-y-auto pr-1">
              {Object.entries(configMap.data || {}).map(([key, val]) => (
                <div
                  key={key}
                  className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 space-y-1"
                >
                  <div className="flex items-center justify-between">
                    <span className="font-mono font-semibold text-xs text-sky-300">{key}</span>
                    <button
                      type="button"
                      onClick={() => handleCopy(val, key)}
                      className="text-zinc-500 hover:text-zinc-200 p-1 text-[11px] flex items-center gap-1 cursor-pointer"
                      title="Copy value"
                    >
                      {copiedKey === key ? (
                        <>
                          <Check className="h-3 w-3 text-emerald-400" />
                          <span className="text-emerald-400 text-[10px]">Copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3 w-3" />
                          <span className="text-[10px]">Copy</span>
                        </>
                      )}
                    </button>
                  </div>
                  <pre className="font-mono text-xs text-zinc-200 bg-zinc-900/70 p-2 rounded border border-zinc-800/80 overflow-x-auto whitespace-pre-wrap break-all max-h-48">
                    {val}
                  </pre>
                </div>
              ))}
            </div>
          </div>
        )}
      </DialogBody>
      <DialogFooter className="flex items-center justify-between">
        {isOperator && onEdit && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              onClose()
              onEdit()
            }}
            className="border-amber-800 text-amber-300 hover:bg-amber-950/50 hover:text-white gap-1.5"
          >
            <Edit className="h-3.5 w-3.5" />
            Edit ConfigMap
          </Button>
        )}
        <Button variant="secondary" size="sm" onClick={onClose} className="ml-auto">
          Close
        </Button>
      </DialogFooter>
    </Dialog>
  )
}

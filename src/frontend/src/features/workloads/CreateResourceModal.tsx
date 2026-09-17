import { useState, useEffect, useMemo } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import {
  KeyRound,
  FileText,
  FileCode,
  Eye,
  EyeOff,
  Plus,
  Trash2,
  CheckCircle2,
  AlertTriangle,
  Loader2,
  Play,
  AlignLeft,
  Link2,
} from 'lucide-react'
import { useCreateSecret, useCreateConfigMap } from './useKubernetesResources'
import { useApplyManifestYaml } from './useWorkloads'
import { CreateNamespaceModal } from './CreateNamespaceModal'
import {
  isConnectionString,
  hasUnencodedUriPassword,
  encodeUriPassword,
  decodeUriPassword,
  autoEncodeSecretForSubmission,
} from '../../utils/urlEncoding'

interface CreateResourceModalProps {
  open: boolean
  onClose: () => void
  initialClusterId?: string
  initialNamespace?: string
  initialTab?: 'secret' | 'configmap' | 'yaml'
  availableClusters?: string[]
  availableNamespaces?: string[]
  onSuccess?: (name: string, kind: string) => void
}

interface KeyValueRow {
  id: string
  key: string
  value: string
  masked?: boolean
  multiline?: boolean
}

export function CreateResourceModal({
  open,
  onClose,
  initialClusterId = '',
  initialNamespace = 'default',
  initialTab = 'secret',
  availableClusters = [],
  availableNamespaces = [],
  onSuccess,
}: CreateResourceModalProps) {
  const [activeTab, setActiveTab] = useState<'secret' | 'configmap' | 'yaml'>(initialTab)

  // Target Location
  const [clusterId, setClusterId] = useState(initialClusterId)
  const [namespace, setNamespace] = useState(initialNamespace)
  const [isCreateNamespaceOpen, setIsCreateNamespaceOpen] = useState(false)

  // Secret Form
  const [secretName, setSecretName] = useState('')
  const [secretType, setSecretType] = useState('Opaque')
  const [secretRows, setSecretRows] = useState<KeyValueRow[]>([
    { id: '1', key: '', value: '', masked: true },
  ])

  // ConfigMap Form
  const [configMapName, setConfigMapName] = useState('')
  const [configMapRows, setConfigMapRows] = useState<KeyValueRow[]>([
    { id: '1', key: '', value: '', masked: false },
  ])

  // Raw YAML Manifest
  const [rawYaml, setRawYaml] = useState('')
  const [dryRunResult, setDryRunResult] = useState<{
    success: boolean
    message: string
    affected: string[]
  } | null>(null)
  const [isDryRunning, setIsDryRunning] = useState(false)

  // Status & Previews
  const [showYamlPreview, setShowYamlPreview] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  // Mutations
  const createSecretMutation = useCreateSecret(clusterId)
  const createConfigMapMutation = useCreateConfigMap(clusterId)
  const applyManifestMutation = useApplyManifestYaml()

  useEffect(() => {
    if (open) {
      setActiveTab(initialTab)
      setClusterId(initialClusterId || availableClusters[0] || '')
      setNamespace(initialNamespace || availableNamespaces[0] || 'default')
      setSecretName('')
      setSecretType('Opaque')
      setSecretRows([{ id: '1', key: '', value: '', masked: true }])
      setConfigMapName('')
      setConfigMapRows([{ id: '1', key: '', value: '', masked: false }])
      setRawYaml('')
      setDryRunResult(null)
      setErrorMessage(null)
      setShowYamlPreview(false)
    }
  }, [open, initialClusterId, initialNamespace, initialTab, availableClusters, availableNamespaces])

  // When Secret Type changes, populate standard template keys
  const handleSecretTypeChange = (newType: string) => {
    setSecretType(newType)
    if (newType === 'kubernetes.io/tls') {
      setSecretRows([
        { id: '1', key: 'tls.crt', value: '', masked: false, multiline: true },
        { id: '2', key: 'tls.key', value: '', masked: true, multiline: true },
      ])
    } else if (newType === 'kubernetes.io/basic-auth') {
      setSecretRows([
        { id: '1', key: 'username', value: '', masked: false },
        { id: '2', key: 'password', value: '', masked: true },
      ])
    } else if (newType === 'kubernetes.io/dockerconfigjson') {
      setSecretRows([
        { id: '1', key: '.dockerconfigjson', value: '', masked: true, multiline: true },
      ])
    }
  }

  // Generate live preview YAML for Secret
  const generatedSecretYaml = useMemo(() => {
    const validRows = secretRows.filter((r) => r.key.trim().length > 0)
    let yaml = `apiVersion: v1\nkind: Secret\nmetadata:\n  name: ${secretName.trim() || 'my-secret'}\n  namespace: ${namespace}\ntype: ${secretType}\n`
    if (validRows.length > 0) {
      yaml += `stringData:\n`
      for (const row of validRows) {
        const val = row.value.includes('\n')
          ? `|\n    ` + row.value.split('\n').join('\n    ')
          : `"${row.value.replace(/"/g, '\\"')}"`
        yaml += `  ${row.key.trim()}: ${val}\n`
      }
    }
    return yaml
  }, [secretName, namespace, secretType, secretRows])

  // Generate live preview YAML for ConfigMap
  const generatedConfigMapYaml = useMemo(() => {
    const validRows = configMapRows.filter((r) => r.key.trim().length > 0)
    let yaml = `apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: ${configMapName.trim() || 'my-config'}\n  namespace: ${namespace}\n`
    if (validRows.length > 0) {
      yaml += `data:\n`
      for (const row of validRows) {
        const val = row.value.includes('\n')
          ? `|\n    ` + row.value.split('\n').join('\n    ')
          : `"${row.value.replace(/"/g, '\\"')}"`
        yaml += `  ${row.key.trim()}: ${val}\n`
      }
    }
    return yaml
  }, [configMapName, namespace, configMapRows])

  // RFC 1123 resource name validation
  const isSecretNameValid = useMemo(() => {
    if (!secretName) return true
    return /^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*$/.test(secretName)
  }, [secretName])

  const isConfigMapNameValid = useMemo(() => {
    if (!configMapName) return true
    return /^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*$/.test(configMapName)
  }, [configMapName])

  // Starter templates for Raw YAML
  const handleSelectTemplate = (templateKey: string) => {
    switch (templateKey) {
      case 'secret-opaque':
        setRawYaml(`apiVersion: v1
kind: Secret
metadata:
  name: example-secret
  namespace: ${namespace}
type: Opaque
stringData:
  DB_PASSWORD: "super-secure-password"
  API_TOKEN: "abc123token"`)
        break

      case 'secret-tls':
        setRawYaml(`apiVersion: v1
kind: Secret
metadata:
  name: example-tls-cert
  namespace: ${namespace}
type: kubernetes.io/tls
stringData:
  tls.crt: |
    -----BEGIN CERTIFICATE-----
    ...
    -----END CERTIFICATE-----
  tls.key: |
    -----BEGIN PRIVATE KEY-----
    ...
    -----END PRIVATE KEY-----`)
        break

      case 'configmap':
        setRawYaml(`apiVersion: v1
kind: ConfigMap
metadata:
  name: example-config
  namespace: ${namespace}
data:
  APP_ENV: "production"
  LOG_LEVEL: "info"
  config.json: |
    {
      "features": {
        "beta": true
      }
    }`)
        break

      case 'pvc':
        setRawYaml(`apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: example-data-pvc
  namespace: ${namespace}
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: longhorn
  resources:
    requests:
      storage: 10Gi`)
        break

      case 'serviceaccount':
        setRawYaml(`apiVersion: v1
kind: ServiceAccount
metadata:
  name: example-sa
  namespace: ${namespace}`)
        break

      case 'job':
        setRawYaml(`apiVersion: batch/v1
kind: Job
metadata:
  name: example-one-shot-job
  namespace: ${namespace}
spec:
  template:
    spec:
      containers:
        - name: task
          image: busybox:latest
          command: ["sh", "-c", "echo 'Running job...' && sleep 2"]
      restartPolicy: OnFailure
  backoffLimit: 3`)
        break
    }
  }

  // Submit Secret
  const handleCreateSecret = async () => {
    if (!secretName.trim()) {
      setErrorMessage('Secret name is required.')
      return
    }
    setErrorMessage(null)

    const stringData: Record<string, string> = {}
    for (const r of secretRows) {
      if (r.key.trim()) {
        stringData[r.key.trim()] = autoEncodeSecretForSubmission(r.value)
      }
    }

    try {
      const res = await createSecretMutation.mutateAsync({
        name: secretName.trim(),
        namespace,
        type: secretType,
        stringData,
      })
      if (res.success) {
        onSuccess?.(secretName.trim(), 'Secret')
        onClose()
      } else {
        setErrorMessage(res.message || 'Failed to create secret.')
      }
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Error creating secret.')
    }
  }

  // Submit ConfigMap
  const handleCreateConfigMap = async () => {
    if (!configMapName.trim()) {
      setErrorMessage('ConfigMap name is required.')
      return
    }
    setErrorMessage(null)

    const data: Record<string, string> = {}
    for (const r of configMapRows) {
      if (r.key.trim()) {
        data[r.key.trim()] = r.value
      }
    }

    try {
      const res = await createConfigMapMutation.mutateAsync({
        name: configMapName.trim(),
        namespace,
        data,
      })
      if (res.success) {
        onSuccess?.(configMapName.trim(), 'ConfigMap')
        onClose()
      } else {
        setErrorMessage(res.message || 'Failed to create config map.')
      }
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Error creating config map.')
    }
  }

  // Dry Run YAML
  const handleDryRunYaml = async () => {
    if (!rawYaml.trim()) return
    setIsDryRunning(true)
    setDryRunResult(null)
    setErrorMessage(null)

    try {
      const res = await applyManifestMutation.mutateAsync({
        clusterId,
        yamlContent: rawYaml,
        dryRun: true,
      })
      setDryRunResult({
        success: res.success,
        message: res.message,
        affected: res.affectedResources || [],
      })
    } catch (err: unknown) {
      setDryRunResult({
        success: false,
        message: err instanceof Error ? err.message : 'Dry run failed.',
        affected: [],
      })
    } finally {
      setIsDryRunning(false)
    }
  }

  // Apply Raw YAML
  const handleApplyRawYaml = async () => {
    if (!rawYaml.trim()) {
      setErrorMessage('Please enter valid Kubernetes YAML manifest.')
      return
    }
    setErrorMessage(null)

    try {
      const res = await applyManifestMutation.mutateAsync({
        clusterId,
        yamlContent: rawYaml,
        dryRun: false,
      })
      if (res.success) {
        onSuccess?.('Manifest', 'YAML')
        onClose()
      } else {
        setErrorMessage(res.message || 'Failed to apply manifest.')
      }
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Error applying manifest.')
    }
  }

  const isSubmitting =
    createSecretMutation.isPending ||
    createConfigMapMutation.isPending ||
    applyManifestMutation.isPending

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="xl">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-xl bg-purple-500/10 border border-purple-500/20 text-purple-400">
              {activeTab === 'secret' && <KeyRound className="h-5 w-5" />}
              {activeTab === 'configmap' && <FileText className="h-5 w-5" />}
              {activeTab === 'yaml' && <FileCode className="h-5 w-5" />}
            </div>
            <div>
              <DialogTitle className="text-zinc-100 font-semibold text-base">
                Create Kubernetes Resource
              </DialogTitle>
              <p className="text-xs text-zinc-400">
                Define individual Secrets, ConfigMaps, or arbitrary YAML manifests for Helm prerequisites & workloads
              </p>
            </div>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {/* Top Tabs */}
          <div className="flex items-center gap-1.5 p-1 bg-zinc-950/80 border border-zinc-800 rounded-xl">
            <button
              type="button"
              onClick={() => {
                setActiveTab('secret')
                setErrorMessage(null)
              }}
              className={`flex-1 flex items-center justify-center gap-2 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'secret'
                  ? 'bg-purple-600 text-white shadow-sm'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/50'
              }`}
            >
              <KeyRound className="h-3.5 w-3.5" />
              <span>Secret</span>
            </button>

            <button
              type="button"
              onClick={() => {
                setActiveTab('configmap')
                setErrorMessage(null)
              }}
              className={`flex-1 flex items-center justify-center gap-2 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'configmap'
                  ? 'bg-sky-600 text-white shadow-sm'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/50'
              }`}
            >
              <FileText className="h-3.5 w-3.5" />
              <span>ConfigMap</span>
            </button>

            <button
              type="button"
              onClick={() => {
                setActiveTab('yaml')
                setErrorMessage(null)
              }}
              className={`flex-1 flex items-center justify-center gap-2 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                activeTab === 'yaml'
                  ? 'bg-emerald-600 text-white shadow-sm'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/50'
              }`}
            >
              <FileCode className="h-3.5 w-3.5" />
              <span>Raw Manifest (YAML)</span>
            </button>
          </div>

          {/* Cluster & Namespace Pickers */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 bg-zinc-900/40 border border-zinc-800/80 rounded-xl">
            {availableClusters.length > 1 ? (
              <div className="space-y-1">
                <label className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                  Target Cluster
                </label>
                <Select
                  value={clusterId}
                  onChange={(e) => setClusterId(e.target.value)}
                  className="bg-zinc-950/70 border-zinc-700 text-xs"
                >
                  {availableClusters.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </Select>
              </div>
            ) : (
              <div className="space-y-1">
                <label className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                  Cluster
                </label>
                <div className="px-3 py-2 bg-zinc-950/50 border border-zinc-800 rounded-lg text-xs font-mono text-zinc-300">
                  {clusterId || 'Default Cluster'}
                </div>
              </div>
            )}

            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <label className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                  Target Namespace
                </label>
                <button
                  type="button"
                  onClick={() => setIsCreateNamespaceOpen(true)}
                  className="text-[10px] text-sky-400 hover:text-sky-300 flex items-center gap-0.5 cursor-pointer"
                >
                  <Plus className="h-2.5 w-2.5" />
                  <span>New Namespace</span>
                </button>
              </div>
              <Select
                value={namespace}
                onChange={(e) => setNamespace(e.target.value)}
                className="bg-zinc-950/70 border-zinc-700 text-xs font-mono"
              >
                {availableNamespaces.map((ns) => (
                  <option key={ns} value={ns}>
                    {ns}
                  </option>
                ))}
                {!availableNamespaces.includes('default') && (
                  <option value="default">default</option>
                )}
              </Select>
            </div>
          </div>

          {/* TAB 1: SECRET */}
          {activeTab === 'secret' && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label className="text-xs font-medium text-zinc-300">
                    Secret Name <span className="text-red-400">*</span>
                  </label>
                  <Input
                    placeholder="e.g. postgres-credentials, api-keys"
                    value={secretName}
                    onChange={(e) => setSecretName(e.target.value.toLowerCase().replace(/\s+/g, '-'))}
                    className="font-mono text-xs bg-zinc-950/70"
                  />
                  <p className="text-[10px] text-zinc-500">
                    Must be lowercase alphanumeric with hyphens or dots (e.g. <span className="font-mono text-purple-300">db-credentials</span>)
                  </p>
                  {!isSecretNameValid && (
                    <p className="text-[11px] text-amber-400 flex items-center gap-1 mt-0.5">
                      <AlertTriangle className="h-3 w-3 shrink-0" />
                      <span>Name must consist of lowercase alphanumeric characters, '-' or '.'.</span>
                    </p>
                  )}
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-medium text-zinc-300">
                    Secret Type
                  </label>
                  <Select
                    value={secretType}
                    onChange={(e) => handleSecretTypeChange(e.target.value)}
                    className="text-xs bg-zinc-950/70 font-mono"
                  >
                    <option value="Opaque">Opaque (Generic Key-Value)</option>
                    <option value="kubernetes.io/tls">TLS Certificate (tls.crt / tls.key)</option>
                    <option value="kubernetes.io/dockerconfigjson">Container Registry (.dockerconfigjson)</option>
                    <option value="kubernetes.io/basic-auth">Basic Auth (username / password)</option>
                  </Select>
                </div>
              </div>

              {/* Key-Value Rows */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    Secret Keys & Values
                  </span>
                  <button
                    type="button"
                    onClick={() =>
                      setSecretRows([
                        ...secretRows,
                        { id: Date.now().toString(), key: '', value: '', masked: true, multiline: false },
                      ])
                    }
                    className="text-xs text-purple-400 hover:text-purple-300 flex items-center gap-1 font-medium cursor-pointer"
                  >
                    <Plus className="h-3 w-3" />
                    <span>Add Entry</span>
                  </button>
                </div>

                {/* Column Headers */}
                <div className="flex items-center gap-2 px-3 text-[10px] font-semibold text-zinc-500 uppercase tracking-wider">
                  <div className="w-44 sm:w-52 shrink-0">Key Name</div>
                  <div className="flex-1 min-w-0">Secret Value</div>
                  <div className="w-16 shrink-0 text-right">Actions</div>
                </div>

                <div className="space-y-2 max-h-64 overflow-y-auto pr-1">
                  {secretRows.map((row, idx) => (
                    <div
                      key={row.id}
                      className="p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/90 space-y-2"
                    >
                      <div className="flex items-center gap-2">
                        {/* Key Name: comfortable width, neutral casing */}
                        <div className="w-44 sm:w-52 shrink-0">
                          <input
                            type="text"
                            placeholder="e.g. password, api_token"
                            value={row.key}
                            onChange={(e) => {
                              const updated = [...secretRows]
                              updated[idx].key = e.target.value
                              setSecretRows(updated)
                            }}
                            className="w-full font-mono text-xs px-3 py-2 rounded-lg bg-zinc-900 border border-zinc-700/80 text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:border-purple-500 focus:ring-1 focus:ring-purple-500/50"
                          />
                        </div>

                        {/* Secret Value: fills remaining space, never crushed */}
                        <div className="flex-1 min-w-0 relative">
                          <input
                            type={row.masked ? 'password' : 'text'}
                            placeholder="Secret value string..."
                            value={row.value}
                            onChange={(e) => {
                              const updated = [...secretRows]
                              updated[idx].value = e.target.value
                              setSecretRows(updated)
                            }}
                            className="w-full font-mono text-xs px-3 py-2 pr-9 rounded-lg bg-zinc-900 border border-zinc-700/80 text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:border-purple-500 focus:ring-1 focus:ring-purple-500/50"
                          />
                          <button
                            type="button"
                            onClick={() => {
                              const updated = [...secretRows]
                              updated[idx].masked = !updated[idx].masked
                              setSecretRows(updated)
                            }}
                            className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-400 hover:text-zinc-200 p-1 cursor-pointer"
                            title={row.masked ? 'Reveal value' : 'Mask value'}
                          >
                            {row.masked ? <Eye className="h-3.5 w-3.5" /> : <EyeOff className="h-3.5 w-3.5" />}
                          </button>
                        </div>

                        {/* Row Actions */}
                        <div className="w-16 shrink-0 flex items-center justify-end gap-1">
                          <button
                            type="button"
                            onClick={() => {
                              const updated = [...secretRows]
                              updated[idx].multiline = !updated[idx].multiline
                              setSecretRows(updated)
                            }}
                            className={`p-1.5 rounded text-xs transition-colors cursor-pointer ${
                              row.multiline
                                ? 'bg-purple-950/70 text-purple-300 border border-purple-800'
                                : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800'
                            }`}
                            title={row.multiline ? 'Collapse multiline' : 'Expand to multiline editor'}
                          >
                            <AlignLeft className="h-3.5 w-3.5" />
                          </button>
                          {secretRows.length > 1 && (
                            <button
                              type="button"
                              onClick={() => setSecretRows(secretRows.filter((r) => r.id !== row.id))}
                              className="text-zinc-500 hover:text-rose-400 p-1.5 rounded hover:bg-zinc-800 transition-colors cursor-pointer"
                              title="Remove entry"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </div>
                      </div>

                      {/* Connection String / URI URL-encoding Assistant */}
                      {isConnectionString(row.value) && (
                        <div className="flex items-center justify-between text-[11px] px-2.5 py-1.5 rounded-md bg-purple-950/20 border border-purple-800/40 text-zinc-300">
                          <div className="flex items-center gap-1.5">
                            <Link2 className="w-3.5 h-3.5 text-purple-400 shrink-0" />
                            <span className="font-semibold text-purple-200">Connection URI detected:</span>
                            {hasUnencodedUriPassword(row.value) ? (
                              <span className="text-amber-400">Special characters will be auto URL-encoded on save</span>
                            ) : (
                              <span className="text-emerald-400">Password is URL-encoded</span>
                            )}
                          </div>
                          <button
                            type="button"
                            onClick={() => {
                              const updated = [...secretRows]
                              if (hasUnencodedUriPassword(row.value)) {
                                updated[idx].value = encodeUriPassword(row.value)
                              } else {
                                updated[idx].value = decodeUriPassword(row.value)
                              }
                              setSecretRows(updated)
                            }}
                            className="text-[11px] font-semibold text-purple-300 hover:text-white px-2 py-0.5 rounded bg-purple-950/60 hover:bg-purple-900 border border-purple-700/50 transition-colors cursor-pointer"
                          >
                            {hasUnencodedUriPassword(row.value) ? 'Encode Now' : 'Decode Password'}
                          </button>
                        </div>
                      )}

                      {/* Multiline expansion textarea */}
                      {row.multiline && (
                        <div className="pt-1">
                          <textarea
                            rows={4}
                            placeholder="Multiline secret content (certificates, private keys, json configs)..."
                            value={row.value}
                            onChange={(e) => {
                              const updated = [...secretRows]
                              updated[idx].value = e.target.value
                              setSecretRows(updated)
                            }}
                            className="w-full font-mono text-xs p-3 rounded-lg bg-zinc-900 border border-zinc-700/80 text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:border-purple-500 focus:ring-1 focus:ring-purple-500/50 resize-y"
                          />
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {/* YAML Preview Accordion */}
              <div className="pt-2 border-t border-zinc-800/60">
                <button
                  type="button"
                  onClick={() => setShowYamlPreview(!showYamlPreview)}
                  className="text-xs text-zinc-400 hover:text-zinc-200 flex items-center gap-1.5 cursor-pointer"
                >
                  <FileCode className="h-3.5 w-3.5 text-purple-400" />
                  <span>{showYamlPreview ? 'Hide Manifest Preview' : 'Show Manifest Preview'}</span>
                </button>
                {showYamlPreview && (
                  <pre className="mt-2 p-3 bg-zinc-950 border border-zinc-800 rounded-lg text-[11px] font-mono text-purple-200/90 overflow-x-auto max-h-44">
                    {generatedSecretYaml}
                  </pre>
                )}
              </div>
            </div>
          )}

          {/* TAB 2: CONFIGMAP */}
          {activeTab === 'configmap' && (
            <div className="space-y-4">
              <div className="space-y-1">
                <label className="text-xs font-medium text-zinc-300">
                  ConfigMap Name <span className="text-red-400">*</span>
                </label>
                <Input
                  placeholder="e.g. app-settings, nginx-conf"
                  value={configMapName}
                  onChange={(e) => setConfigMapName(e.target.value.toLowerCase().replace(/\s+/g, '-'))}
                  className="font-mono text-xs bg-zinc-950/70"
                />
                <p className="text-[10px] text-zinc-500">
                  Must be lowercase alphanumeric with hyphens or dots (e.g. <span className="font-mono text-sky-300">app-settings</span>)
                </p>
                {!isConfigMapNameValid && (
                  <p className="text-[11px] text-amber-400 flex items-center gap-1 mt-0.5">
                    <AlertTriangle className="h-3 w-3 shrink-0" />
                    <span>Name must consist of lowercase alphanumeric characters, '-' or '.'.</span>
                  </p>
                )}
              </div>

              {/* Key-Value Rows */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    Configuration Data Entries
                  </span>
                  <button
                    type="button"
                    onClick={() =>
                      setConfigMapRows([
                        ...configMapRows,
                        { id: Date.now().toString(), key: '', value: '', masked: false },
                      ])
                    }
                    className="text-xs text-sky-400 hover:text-sky-300 flex items-center gap-1 font-medium cursor-pointer"
                  >
                    <Plus className="h-3 w-3" />
                    <span>Add Entry</span>
                  </button>
                </div>

                {/* Column Headers */}
                <div className="flex items-center gap-2 px-3 text-[10px] font-semibold text-zinc-500 uppercase tracking-wider">
                  <div className="w-44 sm:w-52 shrink-0">Key / Filename</div>
                  <div className="flex-1 min-w-0">Configuration Value</div>
                  <div className="w-10 shrink-0 text-right">Action</div>
                </div>

                <div className="space-y-2 max-h-64 overflow-y-auto pr-1">
                  {configMapRows.map((row, idx) => (
                    <div
                      key={row.id}
                      className="p-2.5 rounded-lg bg-zinc-950/60 border border-zinc-800/90 space-y-2"
                    >
                      <div className="flex items-start gap-2">
                        {/* Key or Filename */}
                        <div className="w-44 sm:w-52 shrink-0">
                          <input
                            type="text"
                            placeholder="e.g. config.yaml, app.env"
                            value={row.key}
                            onChange={(e) => {
                              const updated = [...configMapRows]
                              updated[idx].key = e.target.value
                              setConfigMapRows(updated)
                            }}
                            className="w-full font-mono text-xs px-3 py-2 rounded-lg bg-zinc-900 border border-zinc-700/80 text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500/50"
                          />
                        </div>

                        {/* Value Textarea with min-w-0 */}
                        <div className="flex-1 min-w-0">
                          <textarea
                            rows={row.value.includes('\n') ? 3 : 1}
                            placeholder="Configuration value or multiline text..."
                            value={row.value}
                            onChange={(e) => {
                              const updated = [...configMapRows]
                              updated[idx].value = e.target.value
                              setConfigMapRows(updated)
                            }}
                            className="w-full font-mono text-xs px-3 py-2 rounded-lg bg-zinc-900 border border-zinc-700/80 text-zinc-100 placeholder:text-zinc-500 focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500/50 resize-y"
                          />
                        </div>

                        {/* Remove Action */}
                        <div className="w-10 shrink-0 flex items-center justify-end pt-1.5">
                          {configMapRows.length > 1 && (
                            <button
                              type="button"
                              onClick={() => setConfigMapRows(configMapRows.filter((r) => r.id !== row.id))}
                              className="text-zinc-500 hover:text-rose-400 p-1.5 rounded hover:bg-zinc-800 transition-colors cursor-pointer"
                              title="Remove entry"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* YAML Preview Accordion */}
              <div className="pt-2 border-t border-zinc-800/60">
                <button
                  type="button"
                  onClick={() => setShowYamlPreview(!showYamlPreview)}
                  className="text-xs text-zinc-400 hover:text-zinc-200 flex items-center gap-1.5 cursor-pointer"
                >
                  <FileCode className="h-3.5 w-3.5 text-sky-400" />
                  <span>{showYamlPreview ? 'Hide Manifest Preview' : 'Show Manifest Preview'}</span>
                </button>
                {showYamlPreview && (
                  <pre className="mt-2 p-3 bg-zinc-950 border border-zinc-800 rounded-lg text-[11px] font-mono text-sky-200/90 overflow-x-auto max-h-44">
                    {generatedConfigMapYaml}
                  </pre>
                )}
              </div>
            </div>
          )}

          {/* TAB 3: RAW YAML */}
          {activeTab === 'yaml' && (
            <div className="space-y-3">
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2">
                  <span className="text-xs text-zinc-400">Load Template:</span>
                  <Select
                    onChange={(e) => handleSelectTemplate(e.target.value)}
                    className="text-xs bg-zinc-950/80 border-zinc-700 py-1"
                  >
                    <option value="">Choose a starter template...</option>
                    <option value="secret-opaque">Secret (Opaque Key-Value)</option>
                    <option value="secret-tls">Secret (TLS Certificate & Key)</option>
                    <option value="configmap">ConfigMap (Key-Value & Files)</option>
                    <option value="pvc">PersistentVolumeClaim (Longhorn)</option>
                    <option value="serviceaccount">ServiceAccount</option>
                    <option value="job">Batch Job (One-shot execution)</option>
                  </Select>
                </div>

                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={handleDryRunYaml}
                  disabled={!rawYaml.trim() || isDryRunning}
                  className="h-7 text-xs border-zinc-700 gap-1.5 text-zinc-300 hover:text-white"
                >
                  {isDryRunning ? (
                    <Loader2 className="h-3 w-3 animate-spin text-sky-400" />
                  ) : (
                    <Play className="h-3 w-3 text-emerald-400" />
                  )}
                  <span>Test Dry Run</span>
                </Button>
              </div>

              {/* Dry Run Feedback */}
              {dryRunResult && (
                <div
                  className={`p-2.5 rounded-lg border text-xs flex items-start gap-2 ${
                    dryRunResult.success
                      ? 'bg-emerald-950/30 border-emerald-800/60 text-emerald-300'
                      : 'bg-rose-950/30 border-rose-800/60 text-rose-300'
                  }`}
                >
                  {dryRunResult.success ? (
                    <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-400 mt-0.5" />
                  ) : (
                    <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400 mt-0.5" />
                  )}
                  <div className="space-y-0.5">
                    <p className="font-semibold">{dryRunResult.message}</p>
                    {dryRunResult.affected.length > 0 && (
                      <p className="text-[11px] opacity-80">
                        Affected resources: {dryRunResult.affected.join(', ')}
                      </p>
                    )}
                  </div>
                </div>
              )}

              <textarea
                rows={12}
                placeholder="Paste or type raw Kubernetes YAML manifests (supports multi-document with '---')..."
                value={rawYaml}
                onChange={(e) => {
                  setRawYaml(e.target.value)
                  setDryRunResult(null)
                }}
                className="w-full p-3 font-mono text-xs bg-zinc-950 border border-zinc-800 rounded-xl text-zinc-100 placeholder:text-zinc-600 focus:outline-none focus:border-emerald-500 leading-relaxed resize-y"
              />
            </div>
          )}

          {/* Error Message */}
          {errorMessage && (
            <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800/60 text-rose-300 text-xs flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 shrink-0 text-rose-400" />
              <span>{errorMessage}</span>
            </div>
          )}
        </DialogBody>

        <DialogFooter>
          <Button
            variant="outline"
            size="sm"
            onClick={onClose}
            disabled={isSubmitting}
            className="text-xs"
          >
            Cancel
          </Button>

          {activeTab === 'secret' && (
            <Button
              variant="primary"
              size="sm"
              onClick={handleCreateSecret}
              disabled={isSubmitting || !secretName.trim() || !isSecretNameValid}
              className="bg-purple-600 hover:bg-purple-500 text-white text-xs font-semibold gap-1.5"
            >
              {isSubmitting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <KeyRound className="h-3.5 w-3.5" />}
              <span>Create Secret</span>
            </Button>
          )}

          {activeTab === 'configmap' && (
            <Button
              variant="primary"
              size="sm"
              onClick={handleCreateConfigMap}
              disabled={isSubmitting || !configMapName.trim() || !isConfigMapNameValid}
              className="bg-sky-600 hover:bg-sky-500 text-white text-xs font-semibold gap-1.5"
            >
              {isSubmitting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <FileText className="h-3.5 w-3.5" />}
              <span>Create ConfigMap</span>
            </Button>
          )}

          {activeTab === 'yaml' && (
            <Button
              variant="primary"
              size="sm"
              onClick={handleApplyRawYaml}
              disabled={isSubmitting || !rawYaml.trim()}
              className="bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold gap-1.5"
            >
              {isSubmitting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Play className="h-3.5 w-3.5" />}
              <span>Apply Manifest</span>
            </Button>
          )}
        </DialogFooter>
      </Dialog>

      {/* Embedded Create Namespace Modal */}
      {isCreateNamespaceOpen && (
        <CreateNamespaceModal
          open={isCreateNamespaceOpen}
          onClose={() => setIsCreateNamespaceOpen(false)}
          clusterId={clusterId}
          onSuccess={(newNs: string) => {
            setNamespace(newNs)
            setIsCreateNamespaceOpen(false)
          }}
        />
      )}
    </>
  )
}

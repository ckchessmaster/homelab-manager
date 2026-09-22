import { useState, useEffect, useMemo, useRef } from 'react'
import {
  X,
  FileCode,
  SlidersHorizontal,
  CheckCircle2,
  AlertTriangle,
  Loader2,
  Save,
  Play,
  Plus,
  Trash2,
  HardDrive,
  Globe,
  Boxes,
  Key,
  KeyRound,
  Lock,
  Server,
  Network,
  Layers,
  Copy,
  Check,
  RotateCcw,
  FileText,
  ChevronDown,
} from 'lucide-react'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { useApplyManifestYaml, useAppBundle, useResourceYaml } from './useWorkloads'
import { useSecrets } from './useKubernetesResources'
import { CreateNamespaceModal } from './CreateNamespaceModal'
import type { WorkloadSummary, AppPortMapping, AppEnvVar } from '../../api/workloads'

interface ParsedYamlDoc {
  id: string
  kind: string
  name: string
  namespace: string
  content: string
}

function parseYamlDocuments(yamlText: string): ParsedYamlDoc[] {
  if (!yamlText || !yamlText.trim()) return []
  const docs = yamlText
    .split(/(?:^|\n)---\s*(?:\n|$)/)
    .map((d) => d.trim())
    .filter(Boolean)

  return docs.map((doc, idx) => {
    const kindMatch = doc.match(/^\s*kind:\s*([A-Za-z0-9]+)/m)
    const nameMatch = doc.match(/^\s*name:\s*([A-Za-z0-9\-.]+)/m)
    const nsMatch = doc.match(/^\s*namespace:\s*([A-Za-z0-9\-.]+)/m)
    const kind = kindMatch ? kindMatch[1] : 'Resource'
    const docName = nameMatch ? nameMatch[1] : `resource-${idx + 1}`
    const namespace = nsMatch ? nsMatch[1] : ''
    return {
      id: `${kind}-${docName}-${idx}`,
      kind,
      name: docName,
      namespace,
      content: doc,
    }
  })
}

function getResourceIcon(kind?: string) {
  if (!kind) {
    return <FileCode className="h-3.5 w-3.5 text-zinc-400 shrink-0" />
  }
  const k = kind.toLowerCase()
  if (
    k === 'deployment' ||
    k === 'statefulset' ||
    k === 'daemonset' ||
    k === 'job' ||
    k === 'cronjob' ||
    k === 'pod'
  ) {
    return <Server className="h-3.5 w-3.5 text-sky-400 shrink-0" />
  }
  if (k === 'service') {
    return <Network className="h-3.5 w-3.5 text-emerald-400 shrink-0" />
  }
  if (k === 'ingress') {
    return <Globe className="h-3.5 w-3.5 text-violet-400 shrink-0" />
  }
  if (k === 'persistentvolumeclaim' || k === 'storageclass' || k.includes('pvc')) {
    return <HardDrive className="h-3.5 w-3.5 text-amber-400 shrink-0" />
  }
  if (k === 'secret') {
    return <KeyRound className="h-3.5 w-3.5 text-purple-400 shrink-0" />
  }
  if (k === 'configmap') {
    return <FileText className="h-3.5 w-3.5 text-teal-400 shrink-0" />
  }
  return <FileCode className="h-3.5 w-3.5 text-zinc-400 shrink-0" />
}

interface AppEditorDrawerProps {
  open: boolean
  onClose: () => void
  initialWorkload?: WorkloadSummary | null
  availableClusters: string[]
  availableNamespaces: string[]
}

export function AppEditorDrawer({
  open,
  onClose,
  initialWorkload,
  availableClusters,
  availableNamespaces,
}: AppEditorDrawerProps) {
  const [activeTab, setActiveTab] = useState<'form' | 'yaml' | 'diff'>('form')

  // Form State
  const [clusterId, setClusterId] = useState('')
  const [namespace, setNamespace] = useState('default')
  const [isCreateNamespaceOpen, setIsCreateNamespaceOpen] = useState(false)
  const [name, setName] = useState('')
  const [kind, setKind] = useState<'Deployment' | 'StatefulSet'>('Deployment')
  const [replicas, setReplicas] = useState(1)
  const [image, setImage] = useState('')

  // Ports
  const [ports, setPorts] = useState<AppPortMapping[]>([
    { name: 'http', containerPort: 80, servicePort: 80, protocol: 'TCP' },
  ])

  // Ingress
  const [enableIngress, setEnableIngress] = useState(false)
  const [ingressHost, setIngressHost] = useState('')
  const [ingressPath, setIngressPath] = useState('/')
  const [tlsEnabled, setTlsEnabled] = useState(false)

  // Storage (Longhorn)
  const [enableStorage, setEnableStorage] = useState(false)
  const [storageMountPath, setStorageMountPath] = useState('/data')
  const [storageSize, setStorageSize] = useState('10Gi')
  const [storageClass, setStorageClass] = useState('longhorn')

  // Env Vars
  const [envVars, setEnvVars] = useState<AppEnvVar[]>([])

  // Resources
  const [cpuRequest, setCpuRequest] = useState('')
  const [cpuLimit, setCpuLimit] = useState('')
  const [memoryRequest, setMemoryRequest] = useState('')
  const [memoryLimit, setMemoryLimit] = useState('')

  // Raw YAML
  const [rawYaml, setRawYaml] = useState('')
  const [isYamlDirty, setIsYamlDirty] = useState(false)

  // Split YAML sub-resource tab state
  const [selectedResourceDocIndex, setSelectedResourceDocIndex] = useState<'all' | number>('all')
  const [isAddResourceMenuOpen, setIsAddResourceMenuOpen] = useState(false)
  const [yamlCopied, setYamlCopied] = useState(false)
  const addResourceMenuRef = useRef<HTMLDivElement>(null)

  // Mutation and Dry Run state
  const applyMutation = useApplyManifestYaml()
  const [dryRunResult, setDryRunResult] = useState<{ success: boolean; message: string; affected: string[] } | null>(null)
  const [isDryRunning, setIsDryRunning] = useState(false)

  const isEditingSpecificResource = Boolean(initialWorkload)
  const isAppWorkload = !initialWorkload || initialWorkload.kind === 'Deployment' || initialWorkload.kind === 'StatefulSet'

  // Pre-load specific resource YAML if editing an existing workload
  const { data: resourceYamlData, isLoading: isLoadingResourceYaml } = useResourceYaml(
    initialWorkload?.clusterId,
    initialWorkload?.namespace,
    initialWorkload?.name,
    initialWorkload?.kind
  )

  // Pre-load bundle if editing an app workload
  const { data: existingBundle, isLoading: isLoadingBundle } = useAppBundle(
    isAppWorkload ? initialWorkload?.clusterId : undefined,
    isAppWorkload ? initialWorkload?.namespace : undefined,
    isAppWorkload ? initialWorkload?.name : undefined
  )

  useEffect(() => {
    if (initialWorkload) {
      setClusterId(initialWorkload.clusterId)
      setNamespace(initialWorkload.namespace)
      setName(initialWorkload.name)
      setKind((initialWorkload.kind as 'Deployment' | 'StatefulSet') || 'Deployment')
      setReplicas(initialWorkload.desiredReplicas || 1)
      setImage(initialWorkload.images?.[0] || '')
      setActiveTab('yaml')
      setSelectedResourceDocIndex('all')
    } else {
      setActiveTab('form')
      if (availableClusters.length > 0 && !clusterId) {
        setClusterId(availableClusters[0])
      }
      setNamespace('default')
      setName('')
      setKind('Deployment')
      setReplicas(1)
      setImage('')
      setPorts([{ name: 'http', containerPort: 80, servicePort: 80, protocol: 'TCP' }])
      setEnableIngress(false)
      setIngressHost('')
      setIngressPath('/')
      setTlsEnabled(false)
      setEnableStorage(false)
      setStorageMountPath('/data')
      setStorageSize('10Gi')
      setStorageClass('longhorn')
      setEnvVars([])
      setCpuRequest('')
      setCpuLimit('')
      setMemoryRequest('')
      setMemoryLimit('')
      setRawYaml('')
      setIsYamlDirty(false)
      setDryRunResult(null)
      setSelectedResourceDocIndex('all')
    }
  }, [initialWorkload, availableClusters, open, clusterId])

  // Fetch available secrets in target cluster & namespace for secret referencing
  const { data: availableSecrets = [] } = useSecrets(clusterId, namespace)

  // When specific resource YAML arrives, load it into rawYaml
  useEffect(() => {
    if (resourceYamlData?.yamlContent) {
      setRawYaml(resourceYamlData.yamlContent)
      setIsYamlDirty(false)
    }
  }, [resourceYamlData])

  useEffect(() => {
    if (existingBundle && isAppWorkload) {
      setName(existingBundle.name)
      setNamespace(existingBundle.namespace)
      setKind(existingBundle.kind as 'Deployment' | 'StatefulSet')
      setReplicas(existingBundle.replicas)
      setImage(existingBundle.image)
      if (existingBundle.ports?.length > 0) setPorts(existingBundle.ports)
      if (existingBundle.ingressHost) {
        setEnableIngress(true)
        setIngressHost(existingBundle.ingressHost)
        setIngressPath(existingBundle.ingressPath || '/')
        setTlsEnabled(existingBundle.tlsEnabled || false)
      }
      if (existingBundle.volumeMounts?.length > 0) {
        setEnableStorage(true)
        setStorageMountPath(existingBundle.volumeMounts[0].mountPath)
        setStorageSize(existingBundle.volumeMounts[0].storageSize || '10Gi')
        setStorageClass(existingBundle.volumeMounts[0].storageClass || 'longhorn')
      }
      if (existingBundle.environmentVariables) {
        setEnvVars(
          existingBundle.environmentVariables.map((ev) => ({
            key: ev.key || '',
            value: ev.value || '',
            isSecret: ev.isSecret || Boolean(ev.secretName),
            secretName: ev.secretName || '',
            secretKey: ev.secretKey || '',
            configMapName: ev.configMapName || '',
            configMapKey: ev.configMapKey || '',
          }))
        )
      }
      if (existingBundle.cpuRequest) setCpuRequest(existingBundle.cpuRequest)
      if (existingBundle.cpuLimit) setCpuLimit(existingBundle.cpuLimit)
      if (existingBundle.memoryRequest) setMemoryRequest(existingBundle.memoryRequest)
      if (existingBundle.memoryLimit) setMemoryLimit(existingBundle.memoryLimit)
      if (!resourceYamlData?.yamlContent && existingBundle.rawYaml) {
        setRawYaml(existingBundle.rawYaml)
        setIsYamlDirty(false)
      }
    }
  }, [existingBundle, isAppWorkload, resourceYamlData])

  // Generate multi-document YAML from form fields
  const generatedYaml = useMemo(() => {
    const appName = name.trim() || 'my-app'
    const appNs = namespace.trim() || 'default'
    const appImage = image.trim() || 'nginx:alpine'
    const primaryPort = ports[0]?.containerPort || 80
    const primaryServicePort = ports[0]?.servicePort || 80

    const docs: string[] = []

    // 1. Workload (Deployment or StatefulSet)
    let workloadYaml = `apiVersion: apps/v1\nkind: ${kind}\nmetadata:\n  name: ${appName}\n  namespace: ${appNs}\n  labels:\n    app: ${appName}\n    app.kubernetes.io/managed-by: controlplane\nspec:\n  replicas: ${replicas}\n  selector:\n    matchLabels:\n      app: ${appName}\n  template:\n    metadata:\n      labels:\n        app: ${appName}\n    spec:\n      containers:\n      - name: ${appName}\n        image: ${appImage}\n`

    if (ports.length > 0) {
      workloadYaml += `        ports:\n`
      ports.forEach((p) => {
        workloadYaml += `        - name: ${p.name || 'http'}\n          containerPort: ${p.containerPort}\n          protocol: ${p.protocol || 'TCP'}\n`
      })
    }

    if (envVars.length > 0) {
      workloadYaml += `        env:\n`
      envVars.forEach((ev) => {
        const trimmedKey = ev.key.trim()
        if (!trimmedKey) return

        if (ev.isSecret || ev.secretName) {
          workloadYaml += `        - name: ${trimmedKey}\n`
          workloadYaml += `          valueFrom:\n`
          workloadYaml += `            secretKeyRef:\n`
          workloadYaml += `              name: ${ev.secretName?.trim() || ''}\n`
          workloadYaml += `              key: ${ev.secretKey?.trim() || ''}\n`
        } else if (ev.configMapName) {
          workloadYaml += `        - name: ${trimmedKey}\n`
          workloadYaml += `          valueFrom:\n`
          workloadYaml += `            configMapKeyRef:\n`
          workloadYaml += `              name: ${ev.configMapName.trim()}\n`
          workloadYaml += `              key: ${ev.configMapKey?.trim() || ''}\n`
        } else {
          workloadYaml += `        - name: ${trimmedKey}\n          value: "${ev.value || ''}"\n`
        }
      })
    }

    if (cpuRequest || cpuLimit || memoryRequest || memoryLimit) {
      workloadYaml += `        resources:\n`
      if (cpuRequest || memoryRequest) {
        workloadYaml += `          requests:\n`
        if (cpuRequest) workloadYaml += `            cpu: "${cpuRequest}"\n`
        if (memoryRequest) workloadYaml += `            memory: "${memoryRequest}"\n`
      }
      if (cpuLimit || memoryLimit) {
        workloadYaml += `          limits:\n`
        if (cpuLimit) workloadYaml += `            cpu: "${cpuLimit}"\n`
        if (memoryLimit) workloadYaml += `            memory: "${memoryLimit}"\n`
      }
    }

    if (enableStorage) {
      workloadYaml += `        volumeMounts:\n        - name: data-volume\n          mountPath: ${storageMountPath}\n      volumes:\n      - name: data-volume\n        persistentVolumeClaim:\n          claimName: ${appName}-pvc\n`
    }

    docs.push(workloadYaml.trim())

    // 2. Service
    const serviceYaml = `apiVersion: v1\nkind: Service\nmetadata:\n  name: ${appName}\n  namespace: ${appNs}\n  labels:\n    app: ${appName}\nspec:\n  type: ClusterIP\n  selector:\n    app: ${appName}\n  ports:\n  - name: ${ports[0]?.name || 'http'}\n    port: ${primaryServicePort}\n    targetPort: ${primaryPort}\n    protocol: ${ports[0]?.protocol || 'TCP'}`
    docs.push(serviceYaml.trim())

    // 3. Ingress (NGINX)
    if (enableIngress && ingressHost.trim()) {
      let ingYaml = `apiVersion: networking.k8s.io/v1\nkind: Ingress\nmetadata:\n  name: ${appName}\n  namespace: ${appNs}\n  annotations:\n    kubernetes.io/ingress.class: nginx\n`
      if (tlsEnabled) {
        ingYaml += `    cert-manager.io/cluster-issuer: letsencrypt-prod\n`
      }
      ingYaml += `spec:\n  rules:\n  - host: ${ingressHost.trim()}\n    http:\n      paths:\n      - path: ${ingressPath.trim() || '/'}\n        pathType: Prefix\n        backend:\n          service:\n            name: ${appName}\n            port:\n              number: ${primaryServicePort}\n`
      if (tlsEnabled) {
        ingYaml += `  tls:\n  - hosts:\n    - ${ingressHost.trim()}\n    secretName: ${appName}-tls\n`
      }
      docs.push(ingYaml.trim())
    }

    // 4. PVC (Longhorn Storage)
    if (enableStorage) {
      const pvcYaml = `apiVersion: v1\nkind: PersistentVolumeClaim\nmetadata:\n  name: ${appName}-pvc\n  namespace: ${appNs}\n  labels:\n    app: ${appName}\nspec:\n  accessModes:\n  - ReadWriteOnce\n  storageClassName: ${storageClass.trim() || 'longhorn'}\n  resources:\n    requests:\n      storage: ${storageSize.trim() || '10Gi'}`
      docs.push(pvcYaml.trim())
    }

    return docs.join('\n---\n')
  }, [
    name,
    namespace,
    kind,
    replicas,
    image,
    ports,
    envVars,
    cpuRequest,
    cpuLimit,
    memoryRequest,
    memoryLimit,
    enableStorage,
    storageMountPath,
    storageSize,
    storageClass,
    enableIngress,
    ingressHost,
    ingressPath,
    tlsEnabled,
  ])

  // Sync generated yaml to rawYaml if creating a new app and user hasn't made custom edits in raw tab
  useEffect(() => {
    if (!initialWorkload && !isYamlDirty) {
      setRawYaml(generatedYaml)
    }
  }, [generatedYaml, isYamlDirty, initialWorkload])

  // Parse YAML into individual documents for multi-resource tabbed editing
  const currentYamlToParse = isYamlDirty ? rawYaml : generatedYaml
  const parsedDocs = useMemo(() => parseYamlDocuments(currentYamlToParse), [currentYamlToParse])

  // Keep selected tab valid if document list shrinks
  useEffect(() => {
    if (typeof selectedResourceDocIndex === 'number' && selectedResourceDocIndex >= parsedDocs.length) {
      setSelectedResourceDocIndex(parsedDocs.length > 0 ? parsedDocs.length - 1 : 'all')
    }
  }, [parsedDocs.length, selectedResourceDocIndex])

  // Close add resource menu on click outside
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (addResourceMenuRef.current && !addResourceMenuRef.current.contains(event.target as Node)) {
        setIsAddResourceMenuOpen(false)
      }
    }
    if (isAddResourceMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside)
      return () => document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [isAddResourceMenuOpen])

  const handleSingleDocChange = (index: number, newDocContent: string) => {
    const currentDocs = parseYamlDocuments(isYamlDirty ? rawYaml : generatedYaml)
    if (index >= 0 && index < currentDocs.length) {
      currentDocs[index].content = newDocContent
      const combined = currentDocs.map((d) => d.content).join('\n---\n')
      setRawYaml(combined)
      setIsYamlDirty(true)
    }
  }

  const handleDeleteDoc = (index: number) => {
    const currentDocs = parseYamlDocuments(isYamlDirty ? rawYaml : generatedYaml)
    if (currentDocs.length <= 1) {
      alert('Cannot remove the primary workload resource.')
      return
    }
    const docToDelete = currentDocs[index]
    if (window.confirm(`Are you sure you want to remove ${docToDelete.kind} (${docToDelete.name}) from the manifest?`)) {
      const removed = currentDocs.filter((_, i) => i !== index)
      const combined = removed.map((d) => d.content).join('\n---\n')
      setRawYaml(combined)
      setIsYamlDirty(true)
      setSelectedResourceDocIndex('all')
    }
  }

  const handleAddResourceTemplate = (templateYaml: string) => {
    const current = (isYamlDirty ? rawYaml : generatedYaml).trim()
    const combined = current ? `${current}\n---\n${templateYaml.trim()}` : templateYaml.trim()
    setRawYaml(combined)
    setIsYamlDirty(true)
    const newDocs = parseYamlDocuments(combined)
    setSelectedResourceDocIndex(newDocs.length - 1)
    setIsAddResourceMenuOpen(false)
  }

  const handleCopyYaml = (text: string) => {
    navigator.clipboard.writeText(text)
    setYamlCopied(true)
    setTimeout(() => setYamlCopied(false), 2000)
  }

  if (!open) return null

  const handleDryRun = async () => {
    if (!clusterId) return
    setIsDryRunning(true)
    setDryRunResult(null)
    try {
      const content = (isEditingSpecificResource && rawYaml) ? rawYaml : (isYamlDirty ? rawYaml : generatedYaml)
      const res = await applyMutation.mutateAsync({
        clusterId,
        yamlContent: content,
        dryRun: true,
      })
      setDryRunResult({
        success: res.success,
        message: res.message,
        affected: res.affectedResources || [],
      })
      setActiveTab('diff')
    } catch (err) {
      setDryRunResult({
        success: false,
        message: err instanceof Error ? err.message : 'Dry run failed',
        affected: [],
      })
      setActiveTab('diff')
    } finally {
      setIsDryRunning(false)
    }
  }

  const handleSave = async () => {
    if (!clusterId) {
      alert('Please select a target cluster.')
      return
    }
    const content = (isEditingSpecificResource && rawYaml) ? rawYaml : (isYamlDirty ? rawYaml : generatedYaml)
    try {
      await applyMutation.mutateAsync({
        clusterId,
        yamlContent: content,
        dryRun: false,
      })
      onClose()
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to apply application setup')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div className="fixed inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />

      {/* Slide-Over Drawer */}
      <div className="relative w-full max-w-3xl bg-zinc-900 border-l border-zinc-800 shadow-2xl flex flex-col h-full z-10 animate-in slide-in-from-right duration-300">
        {/* Drawer Header */}
        <div className="flex items-center justify-between p-4 border-b border-zinc-800/80 bg-zinc-950/60">
          <div className="flex items-center gap-3">
            <div className="h-9 w-9 rounded-lg bg-sky-950/80 border border-sky-800/80 flex items-center justify-center text-sky-400">
              {initialWorkload ? getResourceIcon(initialWorkload.kind) : <Boxes className="h-5 w-5" />}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-bold text-zinc-100">
                  {initialWorkload ? `Edit ${initialWorkload.kind}: ${initialWorkload.name}` : 'Create Application Setup'}
                </h2>
                {initialWorkload && (
                  <span className="text-[10px] font-mono px-2 py-0.5 rounded-full bg-zinc-800 text-amber-300 border border-zinc-700">
                    {initialWorkload.namespace}
                  </span>
                )}
              </div>
              <p className="text-xs text-zinc-400">
                {initialWorkload
                  ? `Cluster: ${initialWorkload.clusterId} • Modify manifest and apply directly to cluster`
                  : 'Configure workload, service, NGINX ingress, and Longhorn storage'}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            {/* Mode Switcher */}
            <div className="flex items-center bg-zinc-900 p-1 rounded-lg border border-zinc-800">
              {isAppWorkload && (
                <button
                  type="button"
                  onClick={() => setActiveTab('form')}
                  className={`flex items-center gap-1 px-3 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer ${
                    activeTab === 'form'
                      ? 'bg-sky-600 text-white shadow-xs'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  <SlidersHorizontal className="h-3.5 w-3.5" />
                  Visual Form
                </button>
              )}
              <button
                type="button"
                onClick={() => setActiveTab('yaml')}
                className={`flex items-center gap-1 px-3 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer ${
                  activeTab === 'yaml'
                    ? 'bg-sky-600 text-white shadow-xs'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <FileCode className="h-3.5 w-3.5" />
                <span>{isEditingSpecificResource ? `${initialWorkload?.kind || 'Resource'} YAML` : 'Raw YAML'}</span>
              </button>
              {dryRunResult && (
                <button
                  type="button"
                  onClick={() => setActiveTab('diff')}
                  className={`flex items-center gap-1 px-3 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer ${
                    activeTab === 'diff'
                      ? 'bg-sky-600 text-white shadow-xs'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  <CheckCircle2 className="h-3.5 w-3.5 text-emerald-400" />
                  Dry Run
                </button>
              )}
            </div>

            <button
              onClick={onClose}
              className="p-1.5 rounded-lg text-zinc-400 hover:text-zinc-100 hover:bg-zinc-800 cursor-pointer"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        </div>

        {/* Drawer Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          {(isLoadingResourceYaml || (isAppWorkload && isLoadingBundle)) && (
            <div className="p-4 rounded-xl bg-sky-950/20 border border-sky-800/40 text-sky-300 text-xs flex items-center gap-2">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span>Fetching live manifest for {initialWorkload?.kind || 'resource'} from cluster...</span>
            </div>
          )}

          {activeTab === 'form' && (
            <div className="space-y-6">
              {/* Target Cluster & Namespace */}
              <div className="grid grid-cols-2 gap-4 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-zinc-300">Target Cluster</label>
                  <Select
                    value={clusterId}
                    onChange={(e) => setClusterId(e.target.value)}
                    className="bg-zinc-900 text-xs"
                  >
                    {availableClusters.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <label className="text-xs font-semibold text-zinc-300">Namespace</label>
                    <button
                      type="button"
                      onClick={() => setIsCreateNamespaceOpen(true)}
                      className="text-[11px] text-sky-400 hover:text-sky-300 flex items-center gap-1 cursor-pointer font-medium"
                    >
                      <Plus className="h-3 w-3" />
                      New
                    </button>
                  </div>
                  {availableNamespaces.length > 0 ? (
                    <Select
                      value={namespace}
                      onChange={(e) => setNamespace(e.target.value)}
                      className="bg-zinc-900 text-xs font-mono"
                    >
                      {availableNamespaces.map((ns) => (
                        <option key={ns} value={ns}>
                          {ns}
                        </option>
                      ))}
                    </Select>
                  ) : (
                    <Input
                      value={namespace}
                      onChange={(e) => setNamespace(e.target.value)}
                      placeholder="default"
                      className="bg-zinc-900 text-xs font-mono"
                    />
                  )}
                </div>
              </div>

              {/* General Workload Info */}
              <div className="space-y-3 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <h3 className="text-xs font-bold text-zinc-200 uppercase tracking-wider flex items-center gap-2">
                  <Boxes className="h-4 w-4 text-sky-400" />
                  Workload Definition
                </h3>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="sm:col-span-2 space-y-1">
                    <label className="text-xs text-zinc-400">Application Name</label>
                    <Input
                      value={name}
                      onChange={(e) => setName(e.target.value)}
                      placeholder="e.g. whoami"
                      className="bg-zinc-900 font-mono text-xs"
                      disabled={Boolean(initialWorkload)}
                    />
                  </div>

                  <div className="space-y-1">
                    <label className="text-xs text-zinc-400">Workload Kind</label>
                    <Select
                      value={kind}
                      onChange={(e) => setKind(e.target.value as 'Deployment' | 'StatefulSet')}
                      className="bg-zinc-900 text-xs"
                    >
                      <option value="Deployment">Deployment</option>
                      <option value="StatefulSet">StatefulSet</option>
                    </Select>
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="sm:col-span-2 space-y-1">
                    <label className="text-xs text-zinc-400">Container Image & Tag</label>
                    <Input
                      value={image}
                      onChange={(e) => setImage(e.target.value)}
                      placeholder="e.g. traefik/whoami:latest"
                      className="bg-zinc-900 font-mono text-xs"
                    />
                  </div>

                  <div className="space-y-1">
                    <label className="text-xs text-zinc-400">Replica Count</label>
                    <Input
                      type="number"
                      min={0}
                      max={100}
                      value={replicas}
                      onChange={(e) => setReplicas(Number(e.target.value) || 0)}
                      className="bg-zinc-900 text-xs"
                    />
                  </div>
                </div>
              </div>

              {/* Ports & Service */}
              <div className="space-y-3 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-zinc-200 uppercase tracking-wider flex items-center gap-2">
                    <Globe className="h-4 w-4 text-emerald-400" />
                    Ports & Service Mapping
                  </h3>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() =>
                      setPorts([
                        ...ports,
                        { name: `port-${ports.length + 1}`, containerPort: 8080, servicePort: 8080, protocol: 'TCP' },
                      ])
                    }
                    className="h-6 text-[11px] px-2 gap-1"
                  >
                    <Plus className="h-3 w-3" /> Add Port
                  </Button>
                </div>

                <div className="space-y-2">
                  {ports.map((p, idx) => (
                    <div key={idx} className="grid grid-cols-4 gap-2 items-center">
                      <Input
                        placeholder="Name"
                        value={p.name}
                        onChange={(e) => {
                          const updated = [...ports]
                          updated[idx].name = e.target.value
                          setPorts(updated)
                        }}
                        className="bg-zinc-900 text-xs font-mono"
                      />
                      <Input
                        type="number"
                        placeholder="Container Port"
                        value={p.containerPort}
                        onChange={(e) => {
                          const updated = [...ports]
                          updated[idx].containerPort = Number(e.target.value) || 0
                          setPorts(updated)
                        }}
                        className="bg-zinc-900 text-xs font-mono"
                      />
                      <Input
                        type="number"
                        placeholder="Service Port"
                        value={p.servicePort}
                        onChange={(e) => {
                          const updated = [...ports]
                          updated[idx].servicePort = Number(e.target.value) || 0
                          setPorts(updated)
                        }}
                        className="bg-zinc-900 text-xs font-mono"
                      />
                      <div className="flex items-center gap-1">
                        <Select
                          value={p.protocol || 'TCP'}
                          onChange={(e) => {
                            const updated = [...ports]
                            updated[idx].protocol = e.target.value
                            setPorts(updated)
                          }}
                          className="bg-zinc-900 text-xs"
                        >
                          <option value="TCP">TCP</option>
                          <option value="UDP">UDP</option>
                        </Select>
                        {ports.length > 1 && (
                          <button
                            type="button"
                            onClick={() => setPorts(ports.filter((_, i) => i !== idx))}
                            className="p-1.5 text-zinc-500 hover:text-rose-400"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Ingress (NGINX) */}
              <div className="space-y-3 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-zinc-200 uppercase tracking-wider flex items-center gap-2">
                    <Globe className="h-4 w-4 text-purple-400" />
                    NGINX Ingress Routing
                  </h3>
                  <label className="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={enableIngress}
                      onChange={(e) => setEnableIngress(e.target.checked)}
                      className="rounded border-zinc-700 text-purple-500 focus:ring-0"
                    />
                    Enable Ingress
                  </label>
                </div>

                {enableIngress && (
                  <div className="space-y-3 pt-2">
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      <div className="space-y-1">
                        <label className="text-xs text-zinc-400">Ingress Hostname</label>
                        <Input
                          value={ingressHost}
                          onChange={(e) => setIngressHost(e.target.value)}
                          placeholder="e.g. app.homelab.local"
                          className="bg-zinc-900 font-mono text-xs"
                        />
                      </div>

                      <div className="space-y-1">
                        <label className="text-xs text-zinc-400">Path</label>
                        <Input
                          value={ingressPath}
                          onChange={(e) => setIngressPath(e.target.value)}
                          placeholder="/"
                          className="bg-zinc-900 font-mono text-xs"
                        />
                      </div>
                    </div>

                    <label className="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer pt-1">
                      <input
                        type="checkbox"
                        checked={tlsEnabled}
                        onChange={(e) => setTlsEnabled(e.target.checked)}
                        className="rounded border-zinc-700 text-sky-500 focus:ring-0"
                      />
                      <span>Enable TLS Certificate (cert-manager / letsencrypt-prod)</span>
                    </label>
                  </div>
                )}
              </div>

              {/* Storage (Longhorn) */}
              <div className="space-y-3 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-zinc-200 uppercase tracking-wider flex items-center gap-2">
                    <HardDrive className="h-4 w-4 text-amber-400" />
                    Longhorn Storage & Persistent Volume
                  </h3>
                  <label className="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={enableStorage}
                      onChange={(e) => setEnableStorage(e.target.checked)}
                      className="rounded border-zinc-700 text-amber-500 focus:ring-0"
                    />
                    Attach PVC Volume
                  </label>
                </div>

                {enableStorage && (
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-2">
                    <div className="space-y-1">
                      <label className="text-xs text-zinc-400">Mount Path in Container</label>
                      <Input
                        value={storageMountPath}
                        onChange={(e) => setStorageMountPath(e.target.value)}
                        placeholder="/data"
                        className="bg-zinc-900 font-mono text-xs"
                      />
                    </div>

                    <div className="space-y-1">
                      <label className="text-xs text-zinc-400">Requested Capacity</label>
                      <Input
                        value={storageSize}
                        onChange={(e) => setStorageSize(e.target.value)}
                        placeholder="10Gi"
                        className="bg-zinc-900 font-mono text-xs"
                      />
                    </div>

                    <div className="space-y-1">
                      <label className="text-xs text-zinc-400">StorageClass</label>
                      <Input
                        value={storageClass}
                        onChange={(e) => setStorageClass(e.target.value)}
                        placeholder="longhorn"
                        className="bg-zinc-900 font-mono text-xs"
                      />
                    </div>
                  </div>
                )}
              </div>

              {/* Environment Variables */}
              <div className="space-y-3 p-4 rounded-xl bg-zinc-950/60 border border-zinc-800">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-zinc-200 uppercase tracking-wider flex items-center gap-2">
                    <Key className="h-4 w-4 text-sky-400" />
                    Environment Variables
                  </h3>
                  <div className="flex items-center gap-1.5">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => setEnvVars([...envVars, { key: '', value: '', isSecret: false }])}
                      className="h-6 text-[11px] px-2 gap-1 text-zinc-300 hover:text-white"
                    >
                      <Plus className="h-3 w-3" /> Add Value
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        const firstSecret = availableSecrets[0]
                        setEnvVars([
                          ...envVars,
                          {
                            key: '',
                            value: '',
                            isSecret: true,
                            secretName: firstSecret?.name || '',
                            secretKey: firstSecret?.keys?.[0] || '',
                          },
                        ])
                      }}
                      className="h-6 text-[11px] px-2 gap-1 border-purple-700/60 bg-purple-950/30 text-purple-300 hover:bg-purple-900/50 hover:text-white"
                    >
                      <KeyRound className="h-3 w-3 text-purple-400" /> Reference Secret
                    </Button>
                  </div>
                </div>

                <div className="space-y-2">
                  {envVars.length === 0 ? (
                    <p className="text-xs text-zinc-500">No environment variables defined.</p>
                  ) : (
                    envVars.map((ev, idx) => {
                      const isSecret = ev.isSecret || Boolean(ev.secretName)
                      const selectedSecret = availableSecrets.find((s) => s.name === ev.secretName)
                      const secretKeys = selectedSecret?.keys || []

                      return (
                        <div
                          key={idx}
                          className={`p-2.5 rounded-lg border space-y-2 transition-all ${
                            isSecret
                              ? 'bg-purple-950/20 border-purple-800/40'
                              : 'bg-zinc-900/50 border-zinc-800/70'
                          }`}
                        >
                          <div className="flex items-center gap-2">
                            <Input
                              placeholder="VARIABLE_NAME"
                              value={ev.key}
                              onChange={(e) => {
                                const updated = [...envVars]
                                updated[idx].key = e.target.value
                                setEnvVars(updated)
                              }}
                              className="bg-zinc-900 text-xs font-mono flex-1"
                            />

                            <div className="flex items-center bg-zinc-950 rounded-lg p-0.5 border border-zinc-800 shrink-0">
                              <button
                                type="button"
                                onClick={() => {
                                  const updated = [...envVars]
                                  updated[idx].isSecret = false
                                  updated[idx].secretName = ''
                                  updated[idx].secretKey = ''
                                  setEnvVars(updated)
                                }}
                                className={`px-2 py-0.5 text-[11px] font-medium rounded transition-all cursor-pointer ${
                                  !isSecret
                                    ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                                    : 'text-zinc-500 hover:text-zinc-300'
                                }`}
                              >
                                Value
                              </button>
                              <button
                                type="button"
                                onClick={() => {
                                  const updated = [...envVars]
                                  updated[idx].isSecret = true
                                  if (!updated[idx].secretName && availableSecrets.length > 0) {
                                    updated[idx].secretName = availableSecrets[0].name
                                    updated[idx].secretKey = availableSecrets[0].keys?.[0] || ''
                                  }
                                  setEnvVars(updated)
                                }}
                                className={`px-2 py-0.5 text-[11px] font-medium rounded flex items-center gap-1 transition-all cursor-pointer ${
                                  isSecret
                                    ? 'bg-purple-900/80 text-purple-200 border border-purple-700/60 shadow-xs'
                                    : 'text-zinc-500 hover:text-zinc-300'
                                }`}
                              >
                                <Lock className="h-3 w-3 text-purple-400" />
                                Secret Ref
                              </button>
                            </div>

                            <button
                              type="button"
                              onClick={() => setEnvVars(envVars.filter((_, i) => i !== idx))}
                              className="p-1.5 text-zinc-500 hover:text-rose-400 rounded-md hover:bg-zinc-800/80 transition-colors shrink-0"
                              title="Delete variable"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          </div>

                          {!isSecret ? (
                            <div>
                              <Input
                                placeholder="Value (literal string)"
                                value={ev.value}
                                onChange={(e) => {
                                  const updated = [...envVars]
                                  updated[idx].value = e.target.value
                                  setEnvVars(updated)
                                }}
                                className="bg-zinc-900 text-xs font-mono"
                              />
                            </div>
                          ) : (
                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-0.5">
                              <div className="space-y-1">
                                <label className="text-[10px] text-zinc-400 font-medium flex items-center gap-1">
                                  <KeyRound className="h-3 w-3 text-purple-400" />
                                  Secret Name
                                </label>
                                {availableSecrets.length > 0 ? (
                                  <Select
                                    value={ev.secretName || ''}
                                    onChange={(e) => {
                                      const updated = [...envVars]
                                      const chosen = e.target.value
                                      updated[idx].secretName = chosen
                                      const found = availableSecrets.find((s) => s.name === chosen)
                                      if (found && found.keys && found.keys.length > 0) {
                                        updated[idx].secretKey = found.keys[0]
                                      }
                                      setEnvVars(updated)
                                    }}
                                    className="bg-zinc-900 text-xs font-mono w-full"
                                  >
                                    <option value="">Select a Secret...</option>
                                    {availableSecrets.map((s) => (
                                      <option key={s.name} value={s.name}>
                                        {s.name} ({s.keysCount} {s.keysCount === 1 ? 'key' : 'keys'})
                                      </option>
                                    ))}
                                  </Select>
                                ) : (
                                  <Input
                                    placeholder="Secret Name (e.g. app-secrets)"
                                    value={ev.secretName || ''}
                                    onChange={(e) => {
                                      const updated = [...envVars]
                                      updated[idx].secretName = e.target.value
                                      setEnvVars(updated)
                                    }}
                                    className="bg-zinc-900 text-xs font-mono"
                                  />
                                )}
                              </div>

                              <div className="space-y-1">
                                <label className="text-[10px] text-zinc-400 font-medium flex items-center gap-1">
                                  <Lock className="h-3 w-3 text-purple-400" />
                                  Secret Key
                                </label>
                                {secretKeys.length > 0 ? (
                                  <Select
                                    value={ev.secretKey || ''}
                                    onChange={(e) => {
                                      const updated = [...envVars]
                                      updated[idx].secretKey = e.target.value
                                      setEnvVars(updated)
                                    }}
                                    className="bg-zinc-900 text-xs font-mono w-full"
                                  >
                                    <option value="">Select Key...</option>
                                    {secretKeys.map((k) => (
                                      <option key={k} value={k}>
                                        {k}
                                      </option>
                                    ))}
                                  </Select>
                                ) : (
                                  <Input
                                    placeholder="Secret Key (e.g. password)"
                                    value={ev.secretKey || ''}
                                    onChange={(e) => {
                                      const updated = [...envVars]
                                      updated[idx].secretKey = e.target.value
                                      setEnvVars(updated)
                                    }}
                                    className="bg-zinc-900 text-xs font-mono"
                                  />
                                )}
                              </div>
                            </div>
                          )}
                        </div>
                      )
                    })
                  )}
                </div>
              </div>
            </div>
          )}

          {activeTab === 'yaml' && (
            <div className="space-y-3 h-full flex flex-col">
              {/* Resource Sub-Navigation Tabs */}
              {(parsedDocs.length > 1 || !initialWorkload) && (
                <div className="flex items-center justify-between gap-2 border-b border-zinc-800 pb-2.5">
                  <div className="flex items-center gap-1.5 overflow-x-auto py-0.5 max-w-full">
                    {parsedDocs.length > 1 && (
                      <>
                        <button
                          type="button"
                          onClick={() => setSelectedResourceDocIndex('all')}
                          className={`px-2.5 py-1.5 rounded-lg text-xs font-medium flex items-center gap-1.5 transition-all shrink-0 cursor-pointer ${
                            selectedResourceDocIndex === 'all'
                              ? 'bg-zinc-800 text-zinc-100 border border-zinc-700 shadow-xs'
                              : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40'
                          }`}
                        >
                          <Layers className="h-3.5 w-3.5 text-sky-400" />
                          <span>All Manifests</span>
                          <span className="px-1.5 py-0.2 bg-zinc-900 border border-zinc-700 rounded-full text-[10px] text-zinc-400">
                            {parsedDocs.length}
                          </span>
                        </button>

                        <div className="h-4 w-px bg-zinc-800 shrink-0 mx-1" />

                        {parsedDocs.map((doc, idx) => {
                          const isSelected = selectedResourceDocIndex === idx
                          return (
                            <button
                              key={doc.id}
                              type="button"
                              onClick={() => setSelectedResourceDocIndex(idx)}
                              className={`px-2.5 py-1.5 rounded-lg text-xs font-medium flex items-center gap-1.5 transition-all shrink-0 cursor-pointer ${
                                isSelected
                                  ? 'bg-zinc-800 text-zinc-100 border border-zinc-700 shadow-xs'
                                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/40'
                              }`}
                            >
                              {getResourceIcon(doc.kind)}
                              <span>{doc.kind}</span>
                              <span className="text-zinc-500 font-normal">({doc.name})</span>
                            </button>
                          )
                        })}
                      </>
                    )}
                  </div>

                  {!initialWorkload && (
                    <div className="flex items-center gap-1.5 shrink-0 relative" ref={addResourceMenuRef}>
                      <div className="relative">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => setIsAddResourceMenuOpen(!isAddResourceMenuOpen)}
                          className="h-7 text-xs px-2 gap-1 border-zinc-700 bg-zinc-800/60 text-zinc-200 hover:bg-zinc-700/60 cursor-pointer"
                        >
                          <Plus className="h-3 w-3" />
                          <span>Add Resource</span>
                          <ChevronDown className="h-3 w-3 text-zinc-400" />
                        </Button>

                        {isAddResourceMenuOpen && (
                          <div className="absolute right-0 top-full mt-1 w-52 bg-zinc-900 border border-zinc-800 rounded-xl shadow-2xl z-30 py-1 text-xs animate-in fade-in zoom-in-95 duration-150">
                            <div className="px-3 py-1.5 text-[10px] uppercase font-semibold text-zinc-500 tracking-wider">
                              Append Resource Document
                            </div>
                            <button
                              type="button"
                              onClick={() => {
                                const appName = name.trim() || 'my-app'
                                const appNs = namespace.trim() || 'default'
                                const svcPort = ports[0]?.servicePort || 80
                                const contPort = ports[0]?.containerPort || 80
                                handleAddResourceTemplate(`apiVersion: v1
kind: Service
metadata:
  name: ${appName}
  namespace: ${appNs}
  labels:
    app: ${appName}
spec:
  type: ClusterIP
  selector:
    app: ${appName}
  ports:
  - name: http
    port: ${svcPort}
    targetPort: ${contPort}
    protocol: TCP`)
                              }}
                              className="w-full text-left px-3 py-1.5 flex items-center gap-2 hover:bg-zinc-800 text-zinc-200 cursor-pointer transition-colors"
                            >
                              <Network className="h-3.5 w-3.5 text-emerald-400" />
                              <span>Service (ClusterIP)</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                const appName = name.trim() || 'my-app'
                                const appNs = namespace.trim() || 'default'
                                const svcPort = ports[0]?.servicePort || 80
                                handleAddResourceTemplate(`apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: ${appName}
  namespace: ${appNs}
  annotations:
    kubernetes.io/ingress.class: nginx
spec:
  rules:
  - host: ${appName}.homelab.local
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: ${appName}
            port:
              number: ${svcPort}`)
                              }}
                              className="w-full text-left px-3 py-1.5 flex items-center gap-2 hover:bg-zinc-800 text-zinc-200 cursor-pointer transition-colors"
                            >
                              <Globe className="h-3.5 w-3.5 text-violet-400" />
                              <span>NGINX Ingress</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                const appName = name.trim() || 'my-app'
                                const appNs = namespace.trim() || 'default'
                                handleAddResourceTemplate(`apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: ${appName}-data
  namespace: ${appNs}
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: longhorn
  resources:
    requests:
      storage: 10Gi`)
                              }}
                              className="w-full text-left px-3 py-1.5 flex items-center gap-2 hover:bg-zinc-800 text-zinc-200 cursor-pointer transition-colors"
                            >
                              <HardDrive className="h-3.5 w-3.5 text-amber-400" />
                              <span>PVC (Longhorn)</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                const appName = name.trim() || 'my-app'
                                const appNs = namespace.trim() || 'default'
                                handleAddResourceTemplate(`apiVersion: v1
kind: ConfigMap
metadata:
  name: ${appName}-config
  namespace: ${appNs}
data:
  APP_ENV: production`)
                              }}
                              className="w-full text-left px-3 py-1.5 flex items-center gap-2 hover:bg-zinc-800 text-zinc-200 cursor-pointer transition-colors"
                            >
                              <FileText className="h-3.5 w-3.5 text-teal-400" />
                              <span>ConfigMap</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                const appName = name.trim() || 'my-app'
                                const appNs = namespace.trim() || 'default'
                                handleAddResourceTemplate(`apiVersion: v1
kind: Secret
metadata:
  name: ${appName}-secrets
  namespace: ${appNs}
type: Opaque
stringData:
  DB_PASSWORD: change-me`)
                              }}
                              className="w-full text-left px-3 py-1.5 flex items-center gap-2 hover:bg-zinc-800 text-zinc-200 cursor-pointer transition-colors"
                            >
                              <KeyRound className="h-3.5 w-3.5 text-purple-400" />
                              <span>Secret (Opaque)</span>
                            </button>
                          </div>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Context Header & Quick Actions */}
              {parsedDocs.length <= 1 ? (
                <div className="flex items-center justify-between text-xs px-1">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-zinc-100 flex items-center gap-1.5">
                      {parsedDocs[0] ? getResourceIcon(parsedDocs[0].kind) : <FileCode className="h-3.5 w-3.5 text-zinc-400" />}
                      <span>{parsedDocs[0]?.kind || initialWorkload?.kind || 'Resource'}</span>
                    </span>
                    <span className="text-zinc-600">/</span>
                    <span className="font-mono text-zinc-300">{parsedDocs[0]?.name || initialWorkload?.name}</span>
                    {(parsedDocs[0]?.namespace || initialWorkload?.namespace) && (
                      <span className="px-1.5 py-0.5 rounded bg-zinc-800 text-[10px] text-zinc-400 font-mono">
                        ns: {parsedDocs[0]?.namespace || initialWorkload?.namespace}
                      </span>
                    )}
                    {isYamlDirty && (
                      <span className="text-amber-400 text-[11px] font-medium">• Unsaved changes</span>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    {!initialWorkload && isYamlDirty && (
                      <button
                        type="button"
                        onClick={() => {
                          if (window.confirm('Reset YAML back to generated form values? Custom edits will be replaced.')) {
                            setRawYaml(generatedYaml)
                            setIsYamlDirty(false)
                          }
                        }}
                        className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-amber-400 hover:text-amber-300 hover:bg-zinc-800 transition-colors cursor-pointer"
                        title="Reset YAML to form fields"
                      >
                        <RotateCcw className="h-3 w-3" />
                        Reset to Form
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => handleCopyYaml(rawYaml)}
                      className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800 transition-colors cursor-pointer"
                      title="Copy manifest YAML"
                    >
                      {yamlCopied ? <Check className="h-3 w-3 text-emerald-400" /> : <Copy className="h-3 w-3" />}
                      {yamlCopied ? 'Copied' : 'Copy'}
                    </button>
                  </div>
                </div>
              ) : selectedResourceDocIndex === 'all' ? (
                <div className="flex items-center justify-between text-xs px-1 text-zinc-400">
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-zinc-300">Multi-Resource Manifest:</span>
                    <span className="text-zinc-500 font-mono text-[11px]">
                      ({parsedDocs.length} resources separated by <code className="text-sky-400">---</code>)
                    </span>
                    {isYamlDirty && (
                      <span className="text-amber-400 text-[11px] font-medium">• Custom edits active</span>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    {isYamlDirty && (
                      <button
                        type="button"
                        onClick={() => {
                          if (window.confirm('Reset YAML back to generated form values? Custom edits will be replaced.')) {
                            setRawYaml(generatedYaml)
                            setIsYamlDirty(false)
                          }
                        }}
                        className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-amber-400 hover:text-amber-300 hover:bg-zinc-800 transition-colors cursor-pointer"
                        title="Reset YAML to form fields"
                      >
                        <RotateCcw className="h-3 w-3" />
                        Reset to Form
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => handleCopyYaml(rawYaml)}
                      className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800 transition-colors cursor-pointer"
                      title="Copy all manifests"
                    >
                      {yamlCopied ? <Check className="h-3 w-3 text-emerald-400" /> : <Copy className="h-3 w-3" />}
                      {yamlCopied ? 'Copied' : 'Copy All'}
                    </button>
                  </div>
                </div>
              ) : (
                <div className="flex items-center justify-between text-xs px-1">
                  <div className="flex items-center gap-2">
                    <span className="text-zinc-400 font-mono text-[11px]">
                      Resource {selectedResourceDocIndex + 1} of {parsedDocs.length}:
                    </span>
                    <span className="font-semibold text-zinc-100 flex items-center gap-1.5">
                      {getResourceIcon(parsedDocs[selectedResourceDocIndex]?.kind || '')}
                      {parsedDocs[selectedResourceDocIndex]?.kind}
                    </span>
                    <span className="text-zinc-600">/</span>
                    <span className="font-mono text-zinc-300">{parsedDocs[selectedResourceDocIndex]?.name}</span>
                    {parsedDocs[selectedResourceDocIndex]?.namespace && (
                      <span className="px-1.5 py-0.5 rounded bg-zinc-800 text-[10px] text-zinc-400 font-mono">
                        ns: {parsedDocs[selectedResourceDocIndex]?.namespace}
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => handleCopyYaml(parsedDocs[selectedResourceDocIndex]?.content || '')}
                      className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-zinc-300 hover:text-zinc-100 hover:bg-zinc-800 transition-colors cursor-pointer"
                      title="Copy this resource YAML"
                    >
                      {yamlCopied ? <Check className="h-3 w-3 text-emerald-400" /> : <Copy className="h-3 w-3" />}
                      {yamlCopied ? 'Copied' : 'Copy'}
                    </button>
                    {parsedDocs.length > 1 && (
                      <button
                        type="button"
                        onClick={() => handleDeleteDoc(selectedResourceDocIndex)}
                        className="flex items-center gap-1 px-2 py-1 rounded text-[11px] text-rose-400 hover:text-rose-300 hover:bg-rose-950/30 transition-colors cursor-pointer"
                        title="Remove this resource from manifest"
                      >
                        <Trash2 className="h-3 w-3" />
                        Remove Resource
                      </button>
                    )}
                  </div>
                </div>
              )}

              {/* Editor Textarea */}
              <div className="flex-1 flex flex-col min-h-[480px]">
                <textarea
                  value={
                    selectedResourceDocIndex === 'all'
                      ? rawYaml
                      : (parsedDocs[selectedResourceDocIndex]?.content || '')
                  }
                  onChange={(e) => {
                    if (selectedResourceDocIndex === 'all') {
                      setRawYaml(e.target.value)
                      setIsYamlDirty(true)
                    } else {
                      handleSingleDocChange(selectedResourceDocIndex, e.target.value)
                    }
                  }}
                  rows={24}
                  className="w-full flex-1 p-3.5 font-mono text-xs bg-zinc-950 text-zinc-200 border border-zinc-800 rounded-xl focus:outline-none focus:border-sky-500 resize-none font-semibold leading-relaxed"
                  spellCheck={false}
                />
              </div>
            </div>
          )}

          {activeTab === 'diff' && (
            <div className="space-y-4">
              <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                {dryRunResult?.success ? (
                  <CheckCircle2 className="h-5 w-5 text-emerald-400" />
                ) : (
                  <AlertTriangle className="h-5 w-5 text-rose-400" />
                )}
                Server Dry-Run Validation
              </h3>

              <div
                className={`p-4 rounded-xl border text-xs ${
                  dryRunResult?.success
                    ? 'bg-emerald-950/20 border-emerald-800/60 text-emerald-300'
                    : 'bg-rose-950/20 border-rose-800/60 text-rose-300'
                }`}
              >
                <div className="font-semibold text-sm mb-1">{dryRunResult?.message}</div>
                {dryRunResult?.affected && dryRunResult.affected.length > 0 && (
                  <div className="mt-2 space-y-1">
                    <span className="text-zinc-400 font-medium">Affected Kubernetes Objects:</span>
                    <ul className="list-disc pl-5 font-mono text-[11px] text-zinc-200">
                      {dryRunResult.affected.map((res, i) => (
                        <li key={i}>{res}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Drawer Footer */}
        <div className="p-4 border-t border-zinc-800 bg-zinc-950/80 flex items-center justify-between gap-3">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleDryRun}
            disabled={isDryRunning || (!name.trim() && !rawYaml.trim())}
            className="gap-1.5 text-xs text-sky-400 border-sky-800/60 hover:bg-sky-950/40"
          >
            {isDryRunning ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Play className="h-3.5 w-3.5" />
            )}
            Dry Run Validation
          </Button>

          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="button"
              size="sm"
              onClick={handleSave}
              disabled={applyMutation.isPending || (!name.trim() && !rawYaml.trim())}
              className="gap-1.5 text-xs bg-sky-600 hover:bg-sky-500 text-white"
            >
              {applyMutation.isPending ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : (
                <Save className="h-3.5 w-3.5" />
              )}
              {initialWorkload ? 'Save Changes' : 'Create & Apply'}
            </Button>
          </div>
        </div>
      </div>

      {/* Create Namespace Modal */}
      {isCreateNamespaceOpen && (
        <CreateNamespaceModal
          open={isCreateNamespaceOpen}
          onClose={() => setIsCreateNamespaceOpen(false)}
          clusterId={clusterId || availableClusters[0] || ''}
          availableClusters={availableClusters}
          onSuccess={(created) => {
            setNamespace(created)
            setIsCreateNamespaceOpen(false)
          }}
        />
      )}
    </div>
  )
}

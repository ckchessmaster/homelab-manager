import { useState, useEffect } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import {
  Globe,
  FileCode,
  SlidersHorizontal,
  Plus,
  Trash2,
  Loader2,
  AlertTriangle,
  Lock,
  ArrowRight,
  Sparkles,
} from 'lucide-react'
import { useIngressDetail, useUpdateIngress } from './useWorkloads'
import type { IngressRulePath } from '../../api/workloads'

export interface EditIngressModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onSuccess?: () => void
}

interface AnnotationRow {
  id: string
  key: string
  value: string
}

export function EditIngressModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onSuccess,
}: EditIngressModalProps) {
  const [activeTab, setActiveTab] = useState<'form' | 'yaml'>('form')

  const { data: ingress, isLoading } = useIngressDetail(clusterId, namespace, name, open)
  const updateIngressMutation = useUpdateIngress(clusterId)

  // Form state
  const [ingressClass, setIngressClass] = useState('nginx')
  const [hosts, setHosts] = useState<string[]>([''])
  const [paths, setPaths] = useState<IngressRulePath[]>([
    { path: '/', pathType: 'Prefix', serviceName: '', servicePort: 80, endpointsCount: 0 },
  ])
  const [tlsEnabled, setTlsEnabled] = useState(false)
  const [tlsSecretName, setTlsSecretName] = useState('')
  const [annotations, setAnnotations] = useState<AnnotationRow[]>([])

  // Raw YAML state
  const [rawYaml, setRawYaml] = useState('')
  const [isYamlDirty, setIsYamlDirty] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [initialized, setInitialized] = useState(false)

  // Pre-fill state when ingress data arrives
  useEffect(() => {
    if (ingress && !initialized) {
      setIngressClass(ingress.ingressClass || 'nginx')
      setHosts(ingress.hosts.length > 0 ? [...ingress.hosts] : [''])
      setPaths(
        ingress.paths.length > 0
          ? ingress.paths.map((p) => ({ ...p }))
          : [{ path: '/', pathType: 'Prefix', serviceName: '', servicePort: 80, endpointsCount: 0 }]
      )
      setTlsEnabled(Boolean(ingress.tlsHosts.length > 0 || ingress.tlsSecretName))
      setTlsSecretName(ingress.tlsSecretName || (ingress.name ? `${ingress.name}-tls` : ''))

      const annoEntries = Object.entries(ingress.annotations || {})
      setAnnotations(
        annoEntries.length > 0
          ? annoEntries.map(([k, v], idx) => ({ id: String(idx + 1), key: k, value: v }))
          : []
      )

      setRawYaml(ingress.rawYaml || '')
      setIsYamlDirty(false)
      setInitialized(true)
    }
  }, [ingress, initialized])

  useEffect(() => {
    if (!open) {
      setInitialized(false)
      setErrorMessage(null)
      setIsYamlDirty(false)
      setActiveTab('form')
    }
  }, [open, name, namespace, clusterId])

  // Host handlers
  const handleAddHost = () => setHosts((prev) => [...prev, ''])
  const handleRemoveHost = (index: number) => {
    setHosts((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))
  }
  const handleHostChange = (index: number, val: string) => {
    setHosts((prev) => prev.map((h, i) => (i === index ? val : h)))
  }

  // Path handlers
  const handleAddPath = () => {
    setPaths((prev) => [
      ...prev,
      { path: '/', pathType: 'Prefix', serviceName: '', servicePort: 80, endpointsCount: 0 },
    ])
  }
  const handleRemovePath = (index: number) => {
    setPaths((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))
  }
  const handlePathChange = (index: number, field: keyof IngressRulePath, val: any) => {
    setPaths((prev) =>
      prev.map((p, i) => (i === index ? { ...p, [field]: val } : p))
    )
  }

  // Annotation handlers
  const handleAddAnnotation = () => {
    setAnnotations((prev) => [...prev, { id: String(Date.now()), key: '', value: '' }])
  }
  const handleRemoveAnnotation = (id: string) => {
    setAnnotations((prev) => prev.filter((a) => a.id !== id))
  }
  const handleAnnotationChange = (id: string, field: 'key' | 'value', val: string) => {
    setAnnotations((prev) =>
      prev.map((a) => (a.id === id ? { ...a, [field]: val } : a))
    )
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    try {
      if (activeTab === 'yaml') {
        if (!rawYaml.trim()) {
          setErrorMessage('Raw YAML content cannot be empty.')
          return
        }
        const res = await updateIngressMutation.mutateAsync({
          namespaceName: namespace,
          name,
          payload: { rawYaml },
        })
        if (res.success) {
          onSuccess?.()
          onClose()
        } else {
          setErrorMessage(res.message || 'Failed to update Ingress.')
        }
      } else {
        const cleanHosts = hosts.map((h) => h.trim()).filter(Boolean)
        const cleanPaths = paths
          .filter((p) => p.serviceName.trim())
          .map((p) => ({
            ...p,
            path: p.path.trim() || '/',
            serviceName: p.serviceName.trim(),
            servicePort: Number(p.servicePort) || 80,
          }))

        if (cleanPaths.length === 0) {
          setErrorMessage('Please specify at least one backend service route with a valid service name.')
          return
        }

        const annoMap: Record<string, string> = {}
        for (const a of annotations) {
          if (a.key.trim()) {
            annoMap[a.key.trim()] = a.value.trim()
          }
        }

        const res = await updateIngressMutation.mutateAsync({
          namespaceName: namespace,
          name,
          payload: {
            ingressClass: ingressClass.trim() || 'nginx',
            hosts: cleanHosts.length > 0 ? cleanHosts : undefined,
            paths: cleanPaths,
            tlsEnabled,
            tlsSecretName: tlsEnabled ? tlsSecretName.trim() || `${name}-tls` : undefined,
            annotations: annoMap,
          },
        })

        if (res.success) {
          onSuccess?.()
          onClose()
        } else {
          setErrorMessage(res.message || 'Failed to update Ingress.')
        }
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'An error occurred while updating the Ingress.')
    }
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xl">
      <form onSubmit={handleSave} className="flex flex-col h-full">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center justify-between w-full pr-6">
            <div className="flex items-center gap-2 text-sky-400">
              <Globe className="h-5 w-5 shrink-0" />
              <DialogTitle className="text-zinc-100 font-mono">
                Edit Ingress: {name}
              </DialogTitle>
              <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
                {namespace}
              </Badge>
            </div>

            {/* Mode Segmented Toggle */}
            <div className="flex items-center bg-zinc-950 p-0.5 rounded-lg border border-zinc-800 text-xs">
              <button
                type="button"
                onClick={() => setActiveTab('form')}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer ${
                  activeTab === 'form'
                    ? 'bg-sky-600 text-white shadow-sm font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <SlidersHorizontal className="h-3.5 w-3.5" />
                <span>Form</span>
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('yaml')}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer ${
                  activeTab === 'yaml'
                    ? 'bg-sky-600 text-white shadow-sm font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <FileCode className="h-3.5 w-3.5" />
                <span>Raw YAML</span>
                {isYamlDirty && <span className="h-1.5 w-1.5 rounded-full bg-amber-400" />}
              </button>
            </div>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4 max-h-[75vh] overflow-y-auto">
          {isLoading ? (
            <div className="p-16 text-center text-xs text-zinc-500 flex flex-col items-center justify-center gap-3">
              <Loader2 className="h-7 w-7 animate-spin text-sky-400" />
              <span>Fetching Ingress specification from cluster...</span>
            </div>
          ) : (
            <>
              {errorMessage && (
                <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800/80 text-rose-300 text-xs flex items-start gap-2.5">
                  <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                  <div className="flex-1 whitespace-pre-wrap font-sans">{errorMessage}</div>
                </div>
              )}

              {activeTab === 'form' ? (
                <div className="space-y-5">
                  {/* General Config */}
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 text-xs">
                    <div>
                      <label className="text-[11px] font-semibold text-zinc-400 block mb-1">
                        Ingress Class
                      </label>
                      <Input
                        value={ingressClass}
                        onChange={(e) => setIngressClass(e.target.value)}
                        placeholder="e.g. nginx, traefik"
                        className="font-mono text-xs bg-zinc-950/80 border-zinc-800 text-zinc-200"
                      />
                      <span className="text-[10px] text-zinc-500 mt-1 block">
                        Target ingress controller (e.g. nginx, traefik, haproxy)
                      </span>
                    </div>

                    <div>
                      <label className="text-[11px] font-semibold text-zinc-400 block mb-1">
                        Namespace
                      </label>
                      <div className="h-9 px-3 flex items-center rounded-md bg-zinc-950/60 border border-zinc-800 text-zinc-300 font-mono text-xs">
                        {namespace}
                      </div>
                      <span className="text-[10px] text-zinc-500 mt-1 block">
                        Target Kubernetes namespace
                      </span>
                    </div>
                  </div>

                  {/* Host Domains */}
                  <div className="space-y-2.5">
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold text-zinc-300 uppercase tracking-wider flex items-center gap-1.5">
                        <Globe className="h-3.5 w-3.5 text-sky-400" />
                        Host Domain(s)
                      </span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddHost}
                        className="h-7 text-xs gap-1 border-dashed border-zinc-700 text-zinc-300 hover:text-white"
                      >
                        <Plus className="h-3 w-3" />
                        Add Host
                      </Button>
                    </div>

                    <div className="space-y-2">
                      {hosts.map((host, idx) => (
                        <div key={idx} className="flex items-center gap-2">
                          <Input
                            placeholder="e.g. app.homelab.local or *.homelab.local"
                            value={host}
                            onChange={(e) => handleHostChange(idx, e.target.value)}
                            className="font-mono text-xs bg-zinc-900/90 border-zinc-800 text-sky-300"
                          />
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={() => handleRemoveHost(idx)}
                            disabled={hosts.length <= 1}
                            className="text-zinc-500 hover:text-rose-400 p-2"
                          >
                            <Trash2 className="h-4 w-4" />
                          </Button>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Backend Routing Paths */}
                  <div className="space-y-2.5">
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold text-zinc-300 uppercase tracking-wider flex items-center gap-1.5">
                        <ArrowRight className="h-3.5 w-3.5 text-emerald-400" />
                        Backend Routing Paths ({paths.length})
                      </span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddPath}
                        className="h-7 text-xs gap-1 border-dashed border-zinc-700 text-zinc-300 hover:text-white"
                      >
                        <Plus className="h-3 w-3" />
                        Add Path
                      </Button>
                    </div>

                    <div className="space-y-2.5">
                      {paths.map((p, idx) => (
                        <div
                          key={idx}
                          className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 grid grid-cols-1 sm:grid-cols-12 gap-2.5 items-center"
                        >
                          <div className="sm:col-span-3">
                            <label className="text-[10px] text-zinc-500 block mb-0.5">Path</label>
                            <Input
                              placeholder="/"
                              value={p.path}
                              onChange={(e) => handlePathChange(idx, 'path', e.target.value)}
                              className="font-mono text-xs bg-zinc-900 border-zinc-800 text-zinc-200 h-8"
                              required
                            />
                          </div>

                          <div className="sm:col-span-3">
                            <label className="text-[10px] text-zinc-500 block mb-0.5">Path Type</label>
                            <select
                              value={p.pathType || 'Prefix'}
                              onChange={(e) => handlePathChange(idx, 'pathType', e.target.value)}
                              className="w-full rounded-md bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 h-8 px-2 focus:outline-none focus:ring-1 focus:ring-sky-500 font-mono"
                            >
                              <option value="Prefix">Prefix</option>
                              <option value="Exact">Exact</option>
                              <option value="ImplementationSpecific">ImplementationSpecific</option>
                            </select>
                          </div>

                          <div className="sm:col-span-4">
                            <label className="text-[10px] text-zinc-500 block mb-0.5">Service Name</label>
                            <Input
                              placeholder="backend-service"
                              value={p.serviceName}
                              onChange={(e) => handlePathChange(idx, 'serviceName', e.target.value)}
                              className="font-mono text-xs bg-zinc-900 border-zinc-800 text-emerald-300 h-8"
                              required
                            />
                          </div>

                          <div className="sm:col-span-1">
                            <label className="text-[10px] text-zinc-500 block mb-0.5">Port</label>
                            <Input
                              type="number"
                              placeholder="80"
                              value={p.servicePort}
                              onChange={(e) => handlePathChange(idx, 'servicePort', Number(e.target.value))}
                              className="font-mono text-xs bg-zinc-900 border-zinc-800 text-zinc-200 h-8 text-center"
                              required
                            />
                          </div>

                          <div className="sm:col-span-1 flex justify-end pt-3 sm:pt-0">
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleRemovePath(idx)}
                              disabled={paths.length <= 1}
                              className="text-zinc-500 hover:text-rose-400 p-1.5 h-8 w-8"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* TLS Configuration */}
                  <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800 space-y-3">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <Lock className="h-4 w-4 text-emerald-400" />
                        <div>
                          <span className="text-xs font-semibold text-zinc-200 block">
                            TLS / HTTPS Termination
                          </span>
                          <span className="text-[10px] text-zinc-400">
                            Terminate SSL traffic using a TLS Secret or cert-manager Certificate
                          </span>
                        </div>
                      </div>
                      <label className="relative inline-flex items-center cursor-pointer">
                        <input
                          type="checkbox"
                          checked={tlsEnabled}
                          onChange={(e) => setTlsEnabled(e.target.checked)}
                          className="sr-only peer"
                        />
                        <div className="w-9 h-5 bg-zinc-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-zinc-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-emerald-600"></div>
                      </label>
                    </div>

                    {tlsEnabled && (
                      <div className="pt-2 border-t border-zinc-800/80">
                        <label className="text-[10px] text-zinc-400 block mb-1">
                          TLS Secret Name
                        </label>
                        <Input
                          value={tlsSecretName}
                          onChange={(e) => setTlsSecretName(e.target.value)}
                          placeholder="e.g. my-app-tls"
                          className="font-mono text-xs bg-zinc-950/80 border-zinc-800 text-emerald-300"
                        />
                      </div>
                    )}
                  </div>

                  {/* Annotations */}
                  <div className="space-y-2.5">
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-semibold text-zinc-300 uppercase tracking-wider flex items-center gap-1.5">
                        <Sparkles className="h-3.5 w-3.5 text-purple-400" />
                        Annotations ({annotations.length})
                      </span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddAnnotation}
                        className="h-7 text-xs gap-1 border-dashed border-zinc-700 text-zinc-300 hover:text-white"
                      >
                        <Plus className="h-3 w-3" />
                        Add Annotation
                      </Button>
                    </div>

                    {annotations.length === 0 ? (
                      <div className="p-4 text-center rounded-lg border border-dashed border-zinc-800 text-xs text-zinc-500">
                        No custom annotations defined. Click "Add Annotation" to configure cert-manager or NGINX tweaks.
                      </div>
                    ) : (
                      <div className="space-y-2">
                        {annotations.map((a) => (
                          <div key={a.id} className="flex items-center gap-2">
                            <Input
                              placeholder="cert-manager.io/cluster-issuer"
                              value={a.key}
                              onChange={(e) => handleAnnotationChange(a.id, 'key', e.target.value)}
                              className="font-mono text-xs w-1/2 bg-zinc-900 border-zinc-800 text-purple-300 h-8"
                            />
                            <Input
                              placeholder="letsencrypt-prod"
                              value={a.value}
                              onChange={(e) => handleAnnotationChange(a.id, 'value', e.target.value)}
                              className="font-mono text-xs w-1/2 bg-zinc-900 border-zinc-800 text-zinc-200 h-8"
                            />
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleRemoveAnnotation(a.id)}
                              className="text-zinc-500 hover:text-rose-400 p-1.5 h-8 w-8"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                /* Raw YAML Editor */
                <div className="space-y-2">
                  <div className="flex items-center justify-between text-xs text-zinc-400">
                    <span>Direct Kubernetes Ingress manifest editor:</span>
                    {isYamlDirty && (
                      <span className="text-amber-400 text-[11px] font-mono">
                        Modified (Unsaved changes)
                      </span>
                    )}
                  </div>
                  <textarea
                    rows={18}
                    value={rawYaml}
                    onChange={(e) => {
                      setRawYaml(e.target.value)
                      setIsYamlDirty(true)
                    }}
                    className="w-full rounded-xl bg-zinc-950 border border-zinc-800 p-4 font-mono text-xs text-sky-200 placeholder:text-zinc-600 focus:outline-none focus:ring-1 focus:ring-sky-500 leading-relaxed"
                    placeholder="apiVersion: networking.k8s.io/v1&#10;kind: Ingress&#10;..."
                  />
                </div>
              )}
            </>
          )}
        </DialogBody>

        <DialogFooter>
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            size="sm"
            disabled={isLoading || updateIngressMutation.isPending}
            className="bg-sky-600 hover:bg-sky-500 text-white font-semibold gap-1.5"
          >
            {updateIngressMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Globe className="h-4 w-4" />
            )}
            Save Ingress Changes
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

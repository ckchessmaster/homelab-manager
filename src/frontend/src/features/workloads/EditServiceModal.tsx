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
import { Select } from '../../components/ui/select'
import { Badge } from '../../components/ui/badge'
import {
  Network,
  FileCode,
  SlidersHorizontal,
  Plus,
  Trash2,
  Loader2,
  AlertTriangle,
  ArrowRight,
} from 'lucide-react'
import { useServiceDetail, useUpdateService } from './useWorkloads'
import type { ServicePort } from '../../api/workloads'

export interface EditServiceModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onSuccess?: () => void
}

interface KeyValueRow {
  id: string
  key: string
  value: string
}

interface EditablePortRow {
  id: string
  name: string
  port: number | string
  targetPort: number | string
  protocol: string
  nodePort?: number | string
}

export function EditServiceModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onSuccess,
}: EditServiceModalProps) {
  const [activeTab, setActiveTab] = useState<'form' | 'yaml'>('form')

  const { data: service, isLoading } = useServiceDetail(clusterId, namespace, name, open)
  const updateServiceMutation = useUpdateService(clusterId)

  // Form state
  const [serviceType, setServiceType] = useState('ClusterIP')
  const [ports, setPorts] = useState<EditablePortRow[]>([
    { id: '1', name: 'http', port: 80, targetPort: 80, protocol: 'TCP' },
  ])
  const [selectors, setSelectors] = useState<KeyValueRow[]>([])
  const [annotations, setAnnotations] = useState<KeyValueRow[]>([])

  // Raw YAML state
  const [rawYaml, setRawYaml] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [initialized, setInitialized] = useState(false)

  // Pre-fill state when service data arrives
  useEffect(() => {
    if (service && !initialized) {
      setServiceType(service.type || 'ClusterIP')

      if (service.ports && service.ports.length > 0) {
        setPorts(
          service.ports.map((p, idx) => ({
            id: String(idx + 1),
            name: p.name || '',
            port: p.port,
            targetPort: p.targetPort || p.port,
            protocol: p.protocol || 'TCP',
            nodePort: p.nodePort ?? '',
          }))
        )
      } else {
        setPorts([{ id: '1', name: 'http', port: 80, targetPort: 80, protocol: 'TCP' }])
      }

      const selectorEntries = Object.entries(service.selector || {})
      setSelectors(
        selectorEntries.length > 0
          ? selectorEntries.map(([k, v], idx) => ({ id: String(idx + 1), key: k, value: v }))
          : []
      )

      const annoEntries = Object.entries(service.annotations || {})
      setAnnotations(
        annoEntries.length > 0
          ? annoEntries.map(([k, v], idx) => ({ id: String(idx + 1), key: k, value: v }))
          : []
      )

      setRawYaml(service.rawYaml || '')
      setInitialized(true)
    }
  }, [service, initialized])

  useEffect(() => {
    if (!open) {
      setInitialized(false)
      setErrorMessage(null)
      setActiveTab('form')
    }
  }, [open, name, namespace, clusterId])

  // Port handlers
  const handleAddPort = () => {
    setPorts((prev) => [
      ...prev,
      {
        id: String(Date.now()),
        name: '',
        port: 80,
        targetPort: 80,
        protocol: 'TCP',
      },
    ])
  }

  const handleRemovePort = (index: number) => {
    setPorts((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))
  }

  const handlePortChange = (index: number, field: keyof EditablePortRow, val: any) => {
    setPorts((prev) =>
      prev.map((p, i) => (i === index ? { ...p, [field]: val } : p))
    )
  }

  // Selector handlers
  const handleAddSelector = () => {
    setSelectors((prev) => [...prev, { id: String(Date.now()), key: '', value: '' }])
  }

  const handleRemoveSelector = (id: string) => {
    setSelectors((prev) => prev.filter((s) => s.id !== id))
  }

  const handleSelectorChange = (id: string, field: 'key' | 'value', val: string) => {
    setSelectors((prev) =>
      prev.map((s) => (s.id === id ? { ...s, [field]: val } : s))
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
        const res = await updateServiceMutation.mutateAsync({
          namespaceName: namespace,
          name,
          payload: { rawYaml },
        })
        if (res.success) {
          onSuccess?.()
          onClose()
        } else {
          setErrorMessage(res.message || 'Failed to update Service.')
        }
      } else {
        const cleanPorts: ServicePort[] = ports
          .filter((p) => Number(p.port) > 0)
          .map((p) => ({
            name: p.name.trim() || undefined,
            port: Number(p.port),
            targetPort: p.targetPort ? String(p.targetPort).trim() : undefined,
            protocol: p.protocol || 'TCP',
            nodePort: p.nodePort ? Number(p.nodePort) : undefined,
          }))

        if (cleanPorts.length === 0) {
          setErrorMessage('Please specify at least one valid port.')
          return
        }

        const selectorMap: Record<string, string> = {}
        for (const s of selectors) {
          if (s.key.trim()) {
            selectorMap[s.key.trim()] = s.value.trim()
          }
        }

        const annoMap: Record<string, string> = {}
        for (const a of annotations) {
          if (a.key.trim()) {
            annoMap[a.key.trim()] = a.value.trim()
          }
        }

        const res = await updateServiceMutation.mutateAsync({
          namespaceName: namespace,
          name,
          payload: {
            type: serviceType,
            ports: cleanPorts,
            selector: Object.keys(selectorMap).length > 0 ? selectorMap : undefined,
            annotations: Object.keys(annoMap).length > 0 ? annoMap : undefined,
          },
        })

        if (res.success) {
          onSuccess?.()
          onClose()
        } else {
          setErrorMessage(res.message || 'Failed to update Service.')
        }
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'An unexpected error occurred while saving the Service.')
    }
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xl">
      <form onSubmit={handleSave} className="flex flex-col h-full">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center justify-between w-full pr-6">
            <div className="flex items-center gap-2 text-sky-400">
              <Network className="h-5 w-5 shrink-0" />
              <DialogTitle className="text-zinc-100 font-mono">
                Edit Service: {name}
              </DialogTitle>
              <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
                {namespace}
              </Badge>
            </div>

            {/* Mode Switcher Tabs */}
            <div className="flex items-center bg-zinc-950 p-0.5 rounded-lg border border-zinc-800 text-xs">
              <button
                type="button"
                onClick={() => setActiveTab('form')}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer ${
                  activeTab === 'form'
                    ? 'bg-sky-600 text-white shadow-xs font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <SlidersHorizontal className="h-3.5 w-3.5" />
                <span>Quick Config</span>
              </button>

              <button
                type="button"
                onClick={() => setActiveTab('yaml')}
                className={`flex items-center gap-1.5 px-3 py-1 rounded-md transition-all cursor-pointer ${
                  activeTab === 'yaml'
                    ? 'bg-sky-600 text-white shadow-xs font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                <FileCode className="h-3.5 w-3.5" />
                <span>Raw YAML</span>
              </button>
            </div>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-6">
          {isLoading ? (
            <div className="py-16 text-center space-y-3">
              <Loader2 className="h-6 w-6 animate-spin mx-auto text-sky-400" />
              <p className="text-xs text-zinc-400 font-mono">Fetching Service configuration from cluster...</p>
            </div>
          ) : (
            <>
              {errorMessage && (
                <div className="p-3 bg-rose-950/40 border border-rose-800/80 rounded-lg text-rose-300 text-xs flex items-start gap-2.5">
                  <AlertTriangle className="h-4 w-4 text-rose-400 shrink-0 mt-0.5" />
                  <span className="font-mono">{errorMessage}</span>
                </div>
              )}

              {activeTab === 'form' ? (
                <>
                  {/* Service Type & Cluster IP Info */}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div className="space-y-1.5">
                      <label className="text-xs font-medium text-zinc-300">Service Type</label>
                      <Select
                        value={serviceType}
                        onChange={(e) => setServiceType(e.target.value)}
                        className="bg-zinc-950 border-zinc-800 text-xs"
                      >
                        <option value="ClusterIP">ClusterIP (Internal Only)</option>
                        <option value="NodePort">NodePort (Host Port Binding)</option>
                        <option value="LoadBalancer">LoadBalancer (Cloud / Metallb External IP)</option>
                        <option value="ExternalName">ExternalName (CNAME Alias)</option>
                      </Select>
                      <p className="text-[11px] text-zinc-500">
                        Determines how this Service is exposed to cluster workloads or external clients.
                      </p>
                    </div>

                    <div className="space-y-1.5">
                      <label className="text-xs font-medium text-zinc-300">Cluster IP (Immutable)</label>
                      <Input
                        value={service?.clusterIp || 'None (Headless)'}
                        disabled
                        className="bg-zinc-950/40 border-zinc-800 text-xs font-mono text-zinc-400 cursor-not-allowed"
                      />
                      <p className="text-[11px] text-zinc-500">
                        Cluster IP is allocated by the Kubernetes controller and preserved automatically.
                      </p>
                    </div>
                  </div>

                  {/* Ports Section */}
                  <div className="space-y-3 pt-2">
                    <div className="flex items-center justify-between">
                      <div>
                        <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider">
                          Service Ports & Target Forwarding
                        </h4>
                        <p className="text-[11px] text-zinc-500 mt-0.5">
                          Define port mappings between the Service and backend target pod container ports.
                        </p>
                      </div>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddPort}
                        className="h-7 text-xs border-zinc-800 bg-zinc-950/60 hover:bg-zinc-800 text-zinc-300 gap-1 cursor-pointer"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add Port
                      </Button>
                    </div>

                    <div className="space-y-2">
                      {ports.map((p, idx) => (
                        <div
                          key={p.id}
                          className="flex flex-wrap md:flex-nowrap items-center gap-2 p-2.5 rounded-lg border border-zinc-800/80 bg-zinc-950/50 text-xs"
                        >
                          <div className="w-28 shrink-0">
                            <Input
                              placeholder="name (e.g. http)"
                              value={p.name}
                              onChange={(e) => handlePortChange(idx, 'name', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-900 border-zinc-800"
                            />
                          </div>

                          <div className="w-24 shrink-0">
                            <Input
                              type="number"
                              placeholder="Port"
                              value={p.port}
                              onChange={(e) => handlePortChange(idx, 'port', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-900 border-zinc-800 text-sky-400 font-semibold"
                            />
                          </div>

                          <ArrowRight className="h-3.5 w-3.5 text-zinc-600 shrink-0 hidden md:block" />

                          <div className="w-28 shrink-0">
                            <Input
                              placeholder="Target Port"
                              value={p.targetPort}
                              onChange={(e) => handlePortChange(idx, 'targetPort', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-900 border-zinc-800 text-zinc-200"
                            />
                          </div>

                          <div className="w-24 shrink-0">
                            <Select
                              value={p.protocol}
                              onChange={(e) => handlePortChange(idx, 'protocol', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-900 border-zinc-800"
                            >
                              <option value="TCP">TCP</option>
                              <option value="UDP">UDP</option>
                              <option value="SCTP">SCTP</option>
                            </Select>
                          </div>

                          {(serviceType === 'NodePort' || serviceType === 'LoadBalancer') && (
                            <div className="w-28 shrink-0">
                              <Input
                                type="number"
                                placeholder="NodePort (opt)"
                                value={p.nodePort ?? ''}
                                onChange={(e) => handlePortChange(idx, 'nodePort', e.target.value)}
                                className="h-8 text-xs font-mono bg-zinc-900 border-zinc-800 text-amber-300"
                              />
                            </div>
                          )}

                          <div className="ml-auto">
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleRemovePort(idx)}
                              disabled={ports.length === 1}
                              className="h-8 w-8 p-0 text-zinc-500 hover:text-rose-400 cursor-pointer disabled:opacity-30"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Selectors Section */}
                  <div className="space-y-3 pt-2">
                    <div className="flex items-center justify-between">
                      <div>
                        <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider">
                          Pod Selector Labels
                        </h4>
                        <p className="text-[11px] text-zinc-500 mt-0.5">
                          Traffic is routed to pods matching all specified key-value label pairs.
                        </p>
                      </div>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddSelector}
                        className="h-7 text-xs border-zinc-800 bg-zinc-950/60 hover:bg-zinc-800 text-zinc-300 gap-1 cursor-pointer"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add Selector
                      </Button>
                    </div>

                    {selectors.length === 0 ? (
                      <div className="p-4 rounded-lg border border-dashed border-zinc-800 bg-zinc-950/30 text-center text-xs text-zinc-500">
                        No selector configured. (Headless / ExternalName service or manual endpoints)
                      </div>
                    ) : (
                      <div className="space-y-2">
                        {selectors.map((s) => (
                          <div key={s.id} className="flex items-center gap-2">
                            <Input
                              placeholder="label key (e.g. app)"
                              value={s.key}
                              onChange={(e) => handleSelectorChange(s.id, 'key', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-950 border-zinc-800 flex-1"
                            />
                            <span className="text-zinc-600 font-mono text-xs">=</span>
                            <Input
                              placeholder="label value (e.g. web)"
                              value={s.value}
                              onChange={(e) => handleSelectorChange(s.id, 'value', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-950 border-zinc-800 flex-1 text-sky-400"
                            />
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleRemoveSelector(s.id)}
                              className="h-8 w-8 p-0 text-zinc-500 hover:text-rose-400 cursor-pointer"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  {/* Annotations Section */}
                  <div className="space-y-3 pt-2">
                    <div className="flex items-center justify-between">
                      <div>
                        <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider">
                          Annotations
                        </h4>
                        <p className="text-[11px] text-zinc-500 mt-0.5">
                          Metadata used by ingress controllers, load balancers, and external DNS.
                        </p>
                      </div>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={handleAddAnnotation}
                        className="h-7 text-xs border-zinc-800 bg-zinc-950/60 hover:bg-zinc-800 text-zinc-300 gap-1 cursor-pointer"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add Annotation
                      </Button>
                    </div>

                    {annotations.length === 0 ? (
                      <div className="p-3 rounded-lg border border-dashed border-zinc-800 bg-zinc-950/30 text-center text-xs text-zinc-500">
                        No custom annotations attached.
                      </div>
                    ) : (
                      <div className="space-y-2">
                        {annotations.map((a) => (
                          <div key={a.id} className="flex items-center gap-2">
                            <Input
                              placeholder="annotation key"
                              value={a.key}
                              onChange={(e) => handleAnnotationChange(a.id, 'key', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-950 border-zinc-800 flex-1"
                            />
                            <span className="text-zinc-600 font-mono text-xs">:</span>
                            <Input
                              placeholder="annotation value"
                              value={a.value}
                              onChange={(e) => handleAnnotationChange(a.id, 'value', e.target.value)}
                              className="h-8 text-xs font-mono bg-zinc-950 border-zinc-800 flex-1"
                            />
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => handleRemoveAnnotation(a.id)}
                              className="h-8 w-8 p-0 text-zinc-500 hover:text-rose-400 cursor-pointer"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </>
              ) : (
                /* Raw YAML Editor Tab */
                <div className="space-y-2">
                  <div className="flex items-center justify-between text-xs text-zinc-400">
                    <span>Full Kubernetes Service YAML Manifest</span>
                    <span className="font-mono text-[11px] text-zinc-500">Cleaned & ready for modification</span>
                  </div>
                  <textarea
                    value={rawYaml}
                    onChange={(e) => setRawYaml(e.target.value)}
                    rows={18}
                    className="w-full bg-zinc-950 border border-zinc-800 rounded-lg p-3 text-xs font-mono text-zinc-200 focus:outline-hidden focus:border-sky-500 focus:ring-1 focus:ring-sky-500 resize-y leading-relaxed"
                    placeholder="apiVersion: v1&#10;kind: Service..."
                    spellCheck={false}
                  />
                  <p className="text-[11px] text-zinc-500">
                    Modifications to Service ports, selectors, and metadata will be reconciled against the cluster. Immutable clusterIP is safely preserved.
                  </p>
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
            disabled={isLoading || updateServiceMutation.isPending}
            className="bg-sky-600 hover:bg-sky-500 text-white font-semibold gap-1.5"
          >
            {updateServiceMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Network className="h-4 w-4" />
            )}
            Save Service Changes
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

import { useState, useMemo, useEffect } from 'react'
import {
  Globe,
  ShieldCheck,
  RefreshCw,
  AlertTriangle,
  Lock,
  ArrowRight,
  ExternalLink,
  Pencil,
  Network,
  Copy,
  Check,
  FileCode,
} from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Select } from '../../components/ui/select'
import {
  TableToolbar,
  TableToolbarSearch,
  TableToolbarGroup,
  TableToolbarActions,
} from '../../components/ui/table-toolbar'
import { useIngresses, useCertificates, useServices } from './useWorkloads'
import { EditIngressModal } from './EditIngressModal'
import { EditServiceModal } from './EditServiceModal'
import { CreateResourceModal } from './CreateResourceModal'
import type { IngressSummary, ServiceSummary } from '../../api/workloads'

interface NetworkIngressViewProps {
  clusterId: string
  selectedNamespace?: string
  onNamespaceChange?: (namespace: string) => void
  availableNamespaces?: string[]
}

type NetworkSubView = 'all' | 'services' | 'ingresses' | 'certificates'

export function NetworkIngressView({
  clusterId,
  selectedNamespace,
  onNamespaceChange,
  availableNamespaces = [],
}: NetworkIngressViewProps) {
  const [searchTerm, setSearchTerm] = useState('')
  const [subView, setSubView] = useState<NetworkSubView>('all')
  const [localNamespace, setLocalNamespace] = useState(selectedNamespace || '')
  const [editingIngress, setEditingIngress] = useState<IngressSummary | null>(null)
  const [editingService, setEditingService] = useState<ServiceSummary | null>(null)
  const [isApplyManifestOpen, setIsApplyManifestOpen] = useState(false)
  const [copiedText, setCopiedText] = useState<string | null>(null)

  // Sync internal namespace state with parent prop when it changes
  useEffect(() => {
    if (selectedNamespace !== undefined) {
      setLocalNamespace(selectedNamespace)
    }
  }, [selectedNamespace])

  const effectiveNamespace = localNamespace || undefined

  const {
    data: services = [],
    isLoading: isServicesLoading,
    isError: isServicesError,
    refetch: refetchServices,
    isFetching: isServicesFetching,
  } = useServices(clusterId, effectiveNamespace)

  const {
    data: ingresses = [],
    isLoading: isIngressLoading,
    isError: isIngressError,
    refetch: refetchIngresses,
    isFetching: isIngressFetching,
  } = useIngresses(clusterId, effectiveNamespace)

  const {
    data: certificates = [],
    isLoading: isCertLoading,
    refetch: refetchCerts,
    isFetching: isCertFetching,
  } = useCertificates(clusterId, effectiveNamespace)

  const [now] = useState(() => Date.now())

  const copyToClipboard = (text: string) => {
    if (navigator.clipboard) {
      navigator.clipboard.writeText(text)
      setCopiedText(text)
      setTimeout(() => setCopiedText(null), 2000)
    }
  }

  const handleNamespaceChange = (ns: string) => {
    setLocalNamespace(ns)
    onNamespaceChange?.(ns)
  }

  const formatAge = (dateStr?: string | null) => {
    if (!dateStr) return '—'
    const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
    if (diff < 60) return `${diff}s`
    if (diff < 3600) return `${Math.floor(diff / 60)}m`
    if (diff < 86400) return `${Math.floor(diff / 3600)}h`
    return `${Math.floor(diff / 86400)}d`
  }

  const getServiceTypeBadge = (type: string) => {
    switch (type) {
      case 'LoadBalancer':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-950/80 text-emerald-300 border border-emerald-800/80">
            LoadBalancer
          </span>
        )
      case 'NodePort':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-950/80 text-sky-300 border border-sky-800/80">
            NodePort
          </span>
        )
      case 'ExternalName':
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold bg-purple-950/80 text-purple-300 border border-purple-800/80">
            ExternalName
          </span>
        )
      case 'ClusterIP':
      default:
        return (
          <span className="inline-flex items-center px-2 py-0.5 rounded text-[10px] font-semibold bg-zinc-800 text-zinc-300 border border-zinc-700">
            ClusterIP
          </span>
        )
    }
  }

  const filteredServices = useMemo(() => {
    const list = Array.isArray(services) ? services : []
    return list.filter((svc) => {
      if (!searchTerm.trim()) return true
      const q = searchTerm.toLowerCase()
      const matchesName = svc.name.toLowerCase().includes(q)
      const matchesNs = svc.namespace.toLowerCase().includes(q)
      const matchesType = svc.type.toLowerCase().includes(q)
      const matchesClusterIp = svc.clusterIp?.toLowerCase().includes(q) ?? false
      const matchesExternalIp = svc.externalIps?.some((ip) => ip.toLowerCase().includes(q)) ?? false
      const matchesPort = svc.ports.some(
        (p) =>
          p.port.toString().includes(q) ||
          (p.targetPort && p.targetPort.toString().toLowerCase().includes(q)) ||
          (p.name && p.name.toLowerCase().includes(q)) ||
          (p.nodePort && p.nodePort.toString().includes(q))
      )
      const matchesSelector = svc.selector
        ? Object.entries(svc.selector).some(
            ([k, v]) =>
              `${k}=${v}`.toLowerCase().includes(q) ||
              k.toLowerCase().includes(q) ||
              v.toLowerCase().includes(q)
          )
        : false
      return (
        matchesName ||
        matchesNs ||
        matchesType ||
        matchesClusterIp ||
        matchesExternalIp ||
        matchesPort ||
        matchesSelector
      )
    })
  }, [services, searchTerm])

  const filteredIngresses = useMemo(() => {
    const list = Array.isArray(ingresses) ? ingresses : []
    return list.filter((ing) => {
      if (!searchTerm.trim()) return true
      const q = searchTerm.toLowerCase()
      return (
        ing.name.toLowerCase().includes(q) ||
        ing.namespace.toLowerCase().includes(q) ||
        ing.hosts.some((h) => h.toLowerCase().includes(q)) ||
        ing.paths.some((p) => p.serviceName.toLowerCase().includes(q))
      )
    })
  }, [ingresses, searchTerm])

  const filteredCerts = useMemo(() => {
    const list = Array.isArray(certificates) ? certificates : []
    return list.filter((cert) => {
      if (!searchTerm.trim()) return true
      const q = searchTerm.toLowerCase()
      return (
        cert.name.toLowerCase().includes(q) ||
        cert.namespace.toLowerCase().includes(q) ||
        (cert.issuer && cert.issuer.toLowerCase().includes(q)) ||
        (cert.secretName && cert.secretName.toLowerCase().includes(q)) ||
        (cert.dnsNames && cert.dnsNames.some((h) => h.toLowerCase().includes(q)))
      )
    })
  }, [certificates, searchTerm])

  const getIssuerBadge = (issuer?: string) => {
    if (!issuer) return <span className="text-zinc-600">-</span>
    const lower = issuer.toLowerCase()
    if (lower.includes('letsencrypt') || lower.includes("let's encrypt")) {
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-sky-950/80 text-sky-300 border border-sky-800/70 text-[11px] font-medium font-sans">
          <ShieldCheck className="h-3 w-3 text-sky-400" />
          Let's Encrypt
        </span>
      )
    }
    if (lower.includes('vault')) {
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-purple-950/80 text-purple-300 border border-purple-800/70 text-[11px] font-medium font-sans">
          <ShieldCheck className="h-3 w-3 text-purple-400" />
          Vault
        </span>
      )
    }
    if (lower.includes('self') || lower.includes('ca')) {
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-amber-950/80 text-amber-300 border border-amber-800/70 text-[11px] font-medium font-sans">
          <ShieldCheck className="h-3 w-3 text-amber-400" />
          {issuer.toLowerCase().includes('self') ? 'Self-Signed' : issuer}
        </span>
      )
    }
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-zinc-800/80 text-zinc-200 border border-zinc-700/60 text-[11px] font-medium font-sans">
        <ShieldCheck className="h-3 w-3 text-zinc-400" />
        {issuer}
      </span>
    )
  }

  const isFetching = isIngressFetching || isCertFetching || isServicesFetching

  return (
    <div className="space-y-6">
      {/* Unified TableToolbar */}
      <TableToolbar>
        <TableToolbarGroup className="flex-1 flex-wrap gap-2.5">
          <TableToolbarSearch
            placeholder="Filter by name, service, port, domain, or IP..."
            value={searchTerm}
            onChange={setSearchTerm}
          />

          {availableNamespaces.length > 0 && (
            <div className="w-36 shrink-0">
              <Select
                value={localNamespace}
                onChange={(e) => handleNamespaceChange(e.target.value)}
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

          {/* Sub-view Segmented Tabs */}
          <div className="flex items-center gap-1 bg-zinc-950/80 p-0.5 rounded-lg border border-zinc-800">
            <button
              type="button"
              onClick={() => setSubView('all')}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer flex items-center gap-1.5 ${
                subView === 'all'
                  ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <span>All</span>
              <span className="text-[10px] px-1.5 py-0.2 rounded-full bg-zinc-900 border border-zinc-700/60 text-zinc-300">
                {services.length + ingresses.length + certificates.length}
              </span>
            </button>

            <button
              type="button"
              onClick={() => setSubView('services')}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer flex items-center gap-1.5 ${
                subView === 'services'
                  ? 'bg-sky-950/80 text-sky-300 border border-sky-800/80 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Network className="h-3.5 w-3.5 text-sky-400" />
              <span>Services</span>
              <span className="text-[10px] px-1.5 py-0.2 rounded-full bg-zinc-900 border border-zinc-700/60 text-sky-300">
                {services.length}
              </span>
            </button>

            <button
              type="button"
              onClick={() => setSubView('ingresses')}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer flex items-center gap-1.5 ${
                subView === 'ingresses'
                  ? 'bg-sky-950/80 text-sky-300 border border-sky-800/80 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Globe className="h-3.5 w-3.5 text-sky-400" />
              <span>Ingress</span>
              <span className="text-[10px] px-1.5 py-0.2 rounded-full bg-zinc-900 border border-zinc-700/60 text-sky-300">
                {ingresses.length}
              </span>
            </button>

            <button
              type="button"
              onClick={() => setSubView('certificates')}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer flex items-center gap-1.5 ${
                subView === 'certificates'
                  ? 'bg-emerald-950/80 text-emerald-300 border border-emerald-800/80 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <ShieldCheck className="h-3.5 w-3.5 text-emerald-400" />
              <span>Certs</span>
              <span className="text-[10px] px-1.5 py-0.2 rounded-full bg-zinc-900 border border-zinc-700/60 text-emerald-300">
                {certificates.length}
              </span>
            </button>
          </div>
        </TableToolbarGroup>

        <TableToolbarActions>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsApplyManifestOpen(true)}
            className="gap-1.5 text-xs h-9 border-zinc-700 bg-zinc-900/80 hover:bg-zinc-800 text-zinc-200 hover:text-sky-300 cursor-pointer"
          >
            <FileCode className="h-3.5 w-3.5 text-sky-400" />
            <span>Apply Manifest</span>
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              refetchServices()
              refetchIngresses()
              refetchCerts()
            }}
            disabled={isFetching}
            className="gap-1.5 text-xs h-9 cursor-pointer"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </TableToolbarActions>
      </TableToolbar>

      {/* 1. Kubernetes Services Section */}
      {(subView === 'all' || subView === 'services') && (
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
              <Network className="h-4 w-4 text-sky-400" />
              Kubernetes Services & Endpoints
            </h3>
            <span className="text-xs text-zinc-500 font-mono">
              {filteredServices.length} of {services.length} services
            </span>
          </div>

          {isServicesLoading ? (
            <div className="p-10 text-center border border-zinc-800 rounded-xl bg-zinc-900/30 text-xs text-zinc-400">
              <RefreshCw className="h-5 w-5 animate-spin mx-auto text-sky-400 mb-2" />
              Discovering Kubernetes services and endpoints...
            </div>
          ) : isServicesError ? (
            <div className="p-6 text-center border border-rose-800/60 rounded-xl bg-rose-950/20 text-rose-300 text-xs flex items-center justify-center gap-2">
              <AlertTriangle className="h-5 w-5 text-rose-400" />
              Failed to query Services from cluster.
            </div>
          ) : filteredServices.length === 0 ? (
            <div className="p-8 text-center border border-zinc-800 rounded-xl bg-zinc-900/40 text-xs text-zinc-400 space-y-1">
              <Network className="h-6 w-6 mx-auto text-zinc-600 mb-2" />
              <div className="font-semibold text-zinc-200">No Services found</div>
              <div>
                {searchTerm
                  ? 'No Kubernetes services match your search filter.'
                  : 'No services found in this cluster / namespace.'}
              </div>
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60 shadow-xs">
              <table className="w-full text-left text-xs text-zinc-300">
                <thead className="bg-zinc-950/80 text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800">
                  <tr>
                    <th className="p-3">Service Name</th>
                    <th className="p-3">Namespace</th>
                    <th className="p-3">Type</th>
                    <th className="p-3">Cluster IP</th>
                    <th className="p-3">External IP / LB</th>
                    <th className="p-3">Ports & Target</th>
                    <th className="p-3">Backends</th>
                    <th className="p-3">Selector</th>
                    <th className="p-3">Age</th>
                    <th className="p-3 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60 font-mono">
                  {filteredServices.map((svc) => (
                    <tr
                      key={`${svc.namespace}-${svc.name}`}
                      className="hover:bg-zinc-800/40 transition-colors"
                    >
                      <td className="p-3 font-semibold text-zinc-100 flex items-center gap-1.5">
                        <Network className="h-3.5 w-3.5 text-sky-400 shrink-0" />
                        <span>{svc.name}</span>
                      </td>
                      <td className="p-3 text-amber-300 font-sans">{svc.namespace}</td>
                      <td className="p-3 font-sans">{getServiceTypeBadge(svc.type)}</td>
                      <td className="p-3">
                        {svc.clusterIp === 'None' ? (
                          <span className="text-zinc-500 italic text-[11px]">None (Headless)</span>
                        ) : svc.clusterIp ? (
                          <div className="flex items-center gap-1.5 font-mono text-[11px] text-zinc-200">
                            <span>{svc.clusterIp}</span>
                            <button
                              type="button"
                              onClick={() => copyToClipboard(svc.clusterIp!)}
                              className="text-zinc-500 hover:text-zinc-300 p-0.5 rounded cursor-pointer transition-colors"
                              title="Copy Cluster IP"
                            >
                              {copiedText === svc.clusterIp ? (
                                <Check className="h-3 w-3 text-emerald-400" />
                              ) : (
                                <Copy className="h-3 w-3" />
                              )}
                            </button>
                          </div>
                        ) : (
                          <span className="text-zinc-600 font-mono">—</span>
                        )}
                      </td>
                      <td className="p-3">
                        {svc.externalIps && svc.externalIps.length > 0 ? (
                          <div className="flex flex-col gap-1">
                            {svc.externalIps.map((ip) => (
                              <div
                                key={ip}
                                className="flex items-center gap-1.5 font-mono text-[11px] text-emerald-300"
                              >
                                <span>{ip}</span>
                                <button
                                  type="button"
                                  onClick={() => copyToClipboard(ip)}
                                  className="text-emerald-500/70 hover:text-emerald-300 p-0.5 rounded cursor-pointer transition-colors"
                                  title="Copy External IP"
                                >
                                  {copiedText === ip ? (
                                    <Check className="h-3 w-3 text-emerald-400" />
                                  ) : (
                                    <Copy className="h-3 w-3" />
                                  )}
                                </button>
                              </div>
                            ))}
                          </div>
                        ) : svc.type === 'LoadBalancer' ? (
                          <span className="text-amber-400 font-sans text-[11px] animate-pulse">
                            Pending...
                          </span>
                        ) : (
                          <span className="text-zinc-600 font-mono">—</span>
                        )}
                      </td>
                      <td className="p-3">
                        {svc.ports.length === 0 ? (
                          <span className="text-zinc-600 font-mono">—</span>
                        ) : (
                          <div className="flex flex-wrap gap-1 max-w-xs">
                            {svc.ports.map((p, idx) => (
                              <span
                                key={idx}
                                className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-zinc-950/80 border border-zinc-800 text-[10px] font-mono text-zinc-300"
                                title={p.name ? `Port Name: ${p.name}` : undefined}
                              >
                                {p.name && <span className="text-zinc-400">{p.name}:</span>}
                                <span className="text-sky-400 font-semibold">{p.port}</span>
                                {p.targetPort && (
                                  <>
                                    <ArrowRight className="h-2.5 w-2.5 text-zinc-600 inline" />
                                    <span className="text-zinc-300">{p.targetPort}</span>
                                  </>
                                )}
                                <span className="text-zinc-500 text-[9px]">/{p.protocol}</span>
                                {p.nodePort ? (
                                  <span className="text-amber-400/90 text-[9px] ml-0.5">
                                    [{p.nodePort}]
                                  </span>
                                ) : null}
                              </span>
                            ))}
                          </div>
                        )}
                      </td>
                      <td className="p-3">
                        {svc.type === 'ExternalName' ? (
                          <span className="text-purple-400 text-[11px] font-mono">
                            External CNAME
                          </span>
                        ) : svc.endpointsCount > 0 ? (
                          <span
                            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-emerald-950/80 text-emerald-300 border border-emerald-800/80 text-[11px] font-mono"
                            title={`${svc.endpointsCount} active pod endpoint(s)`}
                          >
                            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
                            {svc.endpointsCount} ready
                          </span>
                        ) : svc.selector && Object.keys(svc.selector).length > 0 ? (
                          <span
                            className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-amber-950/80 text-amber-300 border border-amber-800/80 text-[11px] font-mono"
                            title="No ready pod endpoints match this service selector"
                          >
                            <AlertTriangle className="h-3 w-3 text-amber-400" />
                            0 endpoints
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-zinc-800/60 text-zinc-400 border border-zinc-700/60 text-[10px] font-mono">
                            No selector
                          </span>
                        )}
                      </td>
                      <td className="p-3">
                        {svc.selector && Object.keys(svc.selector).length > 0 ? (
                          <div className="flex flex-wrap gap-1 max-w-xs">
                            {Object.entries(svc.selector).map(([k, v]) => (
                              <span
                                key={k}
                                className="inline-flex items-center px-1.5 py-0.2 rounded bg-zinc-950/60 text-zinc-400 border border-zinc-800 text-[10px] font-mono truncate"
                                title={`${k}=${v}`}
                              >
                                <span className="text-zinc-500">{k}=</span>
                                <span className="text-zinc-300">{v}</span>
                              </span>
                            ))}
                          </div>
                        ) : (
                          <span className="text-zinc-600 font-mono text-[11px]">—</span>
                        )}
                      </td>
                      <td className="p-3 text-zinc-400 text-[11px] font-mono">
                        {formatAge(svc.creationTimestamp)}
                      </td>
                      <td className="p-3 text-right font-sans">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setEditingService(svc)}
                          className="h-7 px-2.5 text-xs border-zinc-700 bg-zinc-900/80 hover:bg-zinc-800 text-zinc-300 hover:text-sky-300 gap-1.5 cursor-pointer"
                          title={`Edit Service ${svc.name}`}
                        >
                          <Pencil className="h-3 w-3 text-sky-400" />
                          <span>Edit</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* 2. Cert-Manager Certificates Compact DataTable */}
      {(subView === 'all' || subView === 'certificates') && (
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-emerald-400" />
              cert-manager TLS Certificates & Issuers
            </h3>
            <span className="text-xs text-zinc-500 font-mono">
              {filteredCerts.length} of {certificates.length} certificates
            </span>
          </div>

          {isCertLoading && certificates.length === 0 ? (
            <div className="p-10 text-center border border-zinc-800 rounded-xl bg-zinc-900/30 text-xs text-zinc-400">
              <RefreshCw className="h-5 w-5 animate-spin mx-auto text-emerald-400 mb-2" />
              Loading TLS certificates from cert-manager...
            </div>
          ) : filteredCerts.length === 0 ? (
            <div className="p-8 text-center border border-zinc-800 rounded-xl bg-zinc-900/40 text-xs text-zinc-400">
              <Lock className="h-6 w-6 mx-auto text-zinc-600 mb-2" />
              {searchTerm
                ? 'No TLS certificates match your search query.'
                : 'No cert-manager TLS certificates discovered in this cluster/namespace.'}
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60 shadow-xs">
              <table className="w-full text-left text-xs text-zinc-300">
                <thead className="bg-zinc-950/80 text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800">
                  <tr>
                    <th className="p-3">Certificate Name</th>
                    <th className="p-3">Namespace</th>
                    <th className="p-3">Covered Hostname(s)</th>
                    <th className="p-3">Target Secret</th>
                    <th className="p-3">Issuer</th>
                    <th className="p-3">Validity / Expiry</th>
                    <th className="p-3">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60 font-mono">
                  {filteredCerts.map((cert) => {
                    const diffDays = cert.notAfter
                      ? Math.ceil((new Date(cert.notAfter).getTime() - now) / (1000 * 60 * 60 * 24))
                      : null
                    const dateStr = cert.notAfter ? new Date(cert.notAfter).toLocaleDateString() : ''

                    return (
                      <tr
                        key={`${cert.namespace}-${cert.name}`}
                        className="hover:bg-zinc-800/40 transition-colors"
                      >
                        <td className="p-3 font-semibold text-zinc-100 flex items-center gap-1.5">
                          <Lock className="h-3.5 w-3.5 text-emerald-400 shrink-0" />
                          <span>{cert.name}</span>
                        </td>
                        <td className="p-3 text-amber-300 font-sans">{cert.namespace}</td>
                        <td className="p-3">
                          {cert.dnsNames && cert.dnsNames.length > 0 ? (
                            <div className="flex flex-col gap-1 max-w-xs">
                              {cert.dnsNames.map((host) => {
                                const cleanHost = host.replace(/^\*\./, '')
                                return (
                                  <div
                                    key={host}
                                    className="flex items-center gap-1.5 text-sky-400 hover:text-sky-300 font-mono text-[11px]"
                                  >
                                    <Globe className="h-3 w-3 text-sky-500/70 shrink-0" />
                                    <a
                                      href={`https://${cleanHost}`}
                                      target="_blank"
                                      rel="noreferrer"
                                      className="truncate hover:underline flex items-center gap-1"
                                      title={`Open https://${cleanHost}`}
                                    >
                                      <span>{host}</span>
                                      <ExternalLink className="h-2.5 w-2.5 opacity-60 inline shrink-0" />
                                    </a>
                                  </div>
                                )
                              })}
                            </div>
                          ) : (
                            <span className="text-zinc-600 font-mono">—</span>
                          )}
                        </td>
                        <td className="p-3 text-zinc-300">{cert.secretName || '-'}</td>
                        <td className="p-3">{getIssuerBadge(cert.issuer || undefined)}</td>
                        <td className="p-3">
                          {diffDays !== null ? (
                            diffDays < 0 ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-rose-950/80 text-rose-300 border border-rose-800">
                                Expired ({dateStr})
                              </span>
                            ) : diffDays < 3 ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-rose-950/80 text-rose-300 border border-rose-800">
                                {diffDays}d left ({dateStr})
                              </span>
                            ) : diffDays < 14 ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-amber-950/80 text-amber-300 border border-amber-800">
                                {diffDays}d left ({dateStr})
                              </span>
                            ) : (
                              <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-emerald-950/70 text-emerald-300 border border-emerald-800/70">
                                {diffDays}d left ({dateStr})
                              </span>
                            )
                          ) : (
                            <span className="text-zinc-600">-</span>
                          )}
                        </td>
                        <td className="p-3">
                          <Badge
                            variant={cert.isReady ? 'success' : 'warning'}
                            dot
                            className="text-[10px] font-sans"
                          >
                            {cert.isReady ? 'Issued & Ready' : 'Pending / Verifying'}
                          </Badge>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* 3. Ingress Routes Table */}
      {(subView === 'all' || subView === 'ingresses') && (
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
              <Globe className="h-4 w-4 text-sky-400" />
              NGINX Ingress Routing Table
            </h3>
            <span className="text-xs text-zinc-500 font-mono">
              {filteredIngresses.length} of {ingresses.length} routes
            </span>
          </div>

          {isIngressLoading ? (
            <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/30 text-xs text-zinc-400">
              <RefreshCw className="h-6 w-6 animate-spin mx-auto text-sky-400 mb-2" />
              Scanning Kubernetes Ingress objects and endpoints...
            </div>
          ) : isIngressError ? (
            <div className="p-6 text-center border border-rose-800/60 rounded-xl bg-rose-950/20 text-rose-300 text-xs flex items-center justify-center gap-2">
              <AlertTriangle className="h-5 w-5 text-rose-400" />
              Failed to query Ingress resources from cluster.
            </div>
          ) : filteredIngresses.length === 0 ? (
            <div className="p-12 text-center border border-zinc-800 rounded-xl bg-zinc-900/40 text-xs text-zinc-400 space-y-1">
              <Globe className="h-8 w-8 mx-auto text-zinc-600 mb-2" />
              <div className="font-semibold text-zinc-200">No Ingress routes discovered</div>
              <div>Create an Ingress in the App Editor to expose services over HTTP/HTTPS.</div>
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60 shadow-xs">
              <table className="w-full text-left text-xs text-zinc-300">
                <thead className="bg-zinc-950/80 text-[11px] uppercase tracking-wider text-zinc-400 border-b border-zinc-800">
                  <tr>
                    <th className="p-3">Ingress Name</th>
                    <th className="p-3">Namespace</th>
                    <th className="p-3">Host Domain</th>
                    <th className="p-3">Path</th>
                    <th className="p-3">Target Service & Port</th>
                    <th className="p-3">TLS Secret</th>
                    <th className="p-3">Class</th>
                    <th className="p-3 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60 font-mono">
                  {filteredIngresses.map((ing) => (
                    <tr
                      key={`${ing.namespace}-${ing.name}`}
                      className="hover:bg-zinc-800/40 transition-colors"
                    >
                      <td className="p-3 font-semibold text-zinc-100">{ing.name}</td>
                      <td className="p-3 text-amber-300 font-sans">{ing.namespace}</td>
                      <td className="p-3">
                        {ing.hosts.map((h) => (
                          <div key={h} className="flex items-center gap-1 text-sky-400 hover:underline">
                            <a
                              href={`http://${h}`}
                              target="_blank"
                              rel="noreferrer"
                              className="flex items-center gap-1"
                            >
                              {h}
                              <ExternalLink className="h-3 w-3 inline opacity-70" />
                            </a>
                          </div>
                        ))}
                      </td>
                      <td className="p-3 text-zinc-400">
                        {ing.paths.map((p, i) => (
                          <div key={i}>{p.path}</div>
                        ))}
                      </td>
                      <td className="p-3">
                        {ing.paths.map((p, i) => {
                          const isReady = (p.endpointsCount ?? 0) > 0
                          return (
                            <div key={i} className="flex items-center gap-2 py-0.5">
                              <div className="flex items-center gap-1.5 text-zinc-200">
                                <ArrowRight className="h-3 w-3 text-zinc-600 shrink-0" />
                                <span className="font-semibold">{p.serviceName}</span>
                                <span className="text-zinc-500">:{p.servicePort}</span>
                              </div>
                              <span
                                className={`inline-flex items-center px-1.5 py-0.2 rounded-sm text-[10px] font-mono border ${
                                  isReady
                                    ? 'bg-emerald-950/80 text-emerald-300 border-emerald-800/80'
                                    : 'bg-rose-950/80 text-rose-300 border-rose-800/80'
                                }`}
                                title={
                                  isReady
                                    ? `${p.endpointsCount} ready backend endpoint(s)`
                                    : 'No ready endpoints backing this service'
                                }
                              >
                                {isReady
                                  ? `${p.endpointsCount}/${p.endpointsCount} Ready`
                                  : '0/1 Offline'}
                              </span>
                            </div>
                          )
                        })}
                      </td>
                      <td className="p-3">
                        {ing.tlsHosts.length > 0 ? (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-950/60 border border-emerald-800 text-emerald-400 text-[10px] font-sans">
                            <Lock className="h-2.5 w-2.5" /> TLS
                          </span>
                        ) : (
                          <span className="text-zinc-600">None</span>
                        )}
                      </td>
                      <td className="p-3 text-zinc-400">{ing.ingressClass || 'nginx'}</td>
                      <td className="p-3 text-right font-sans">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setEditingIngress(ing)}
                          className="h-7 px-2.5 text-xs border-zinc-700 bg-zinc-900/80 hover:bg-zinc-800 text-zinc-300 hover:text-sky-300 gap-1.5 cursor-pointer"
                          title={`Edit Ingress ${ing.name}`}
                        >
                          <Pencil className="h-3 w-3 text-sky-400" />
                          <span>Edit</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Edit Ingress Modal */}
      {editingIngress && (
        <EditIngressModal
          open={Boolean(editingIngress)}
          onClose={() => setEditingIngress(null)}
          clusterId={clusterId}
          namespace={editingIngress.namespace}
          name={editingIngress.name}
          onSuccess={() => {
            refetchIngresses()
            refetchCerts()
            refetchServices()
          }}
        />
      )}

      {/* Edit Service Modal */}
      {editingService && (
        <EditServiceModal
          open={Boolean(editingService)}
          onClose={() => setEditingService(null)}
          clusterId={clusterId}
          namespace={editingService.namespace}
          name={editingService.name}
          onSuccess={() => {
            refetchServices()
            refetchIngresses()
          }}
        />
      )}

      {/* Create / Apply Manifest Modal */}
      {isApplyManifestOpen && (
        <CreateResourceModal
          open={isApplyManifestOpen}
          onClose={() => setIsApplyManifestOpen(false)}
          initialClusterId={clusterId}
          initialNamespace={effectiveNamespace || 'default'}
          initialTab="yaml"
          availableClusters={[clusterId]}
          availableNamespaces={availableNamespaces}
          onSuccess={() => {
            refetchServices()
            refetchIngresses()
            refetchCerts()
          }}
        />
      )}
    </div>
  )
}

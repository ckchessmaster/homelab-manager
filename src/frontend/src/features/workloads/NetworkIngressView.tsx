import { useState } from 'react'
import {
  Globe,
  ShieldCheck,
  Search,
  RefreshCw,
  AlertTriangle,
  Lock,
  ArrowRight,
  ExternalLink,
} from 'lucide-react'
import { Input } from '../../components/ui/input'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { useIngresses, useCertificates } from './useWorkloads'

interface NetworkIngressViewProps {
  clusterId: string
  selectedNamespace?: string
}

export function NetworkIngressView({ clusterId, selectedNamespace }: NetworkIngressViewProps) {
  const [searchTerm, setSearchTerm] = useState('')

  const {
    data: ingresses = [],
    isLoading: isIngressLoading,
    isError: isIngressError,
    refetch: refetchIngresses,
    isFetching: isIngressFetching,
  } = useIngresses(clusterId, selectedNamespace)

  const {
    data: certificates = [],
    refetch: refetchCerts,
    isFetching: isCertFetching,
  } = useCertificates(clusterId, selectedNamespace)

  const filteredIngresses = ingresses.filter((ing) => {
    if (!searchTerm.trim()) return true
    const q = searchTerm.toLowerCase()
    return (
      ing.name.toLowerCase().includes(q) ||
      ing.namespace.toLowerCase().includes(q) ||
      ing.hosts.some((h) => h.toLowerCase().includes(q))
    )
  })

  const isFetching = isIngressFetching || isCertFetching

  return (
    <div className="space-y-6">
      {/* Top Banner & Refresh */}
      <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between p-4 bg-zinc-900/60 border border-zinc-800/80 rounded-xl backdrop-blur-md">
        <div className="flex flex-1 items-center gap-3">
          <div className="relative min-w-[220px] flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-zinc-500" />
            <Input
              placeholder="Filter routes by domain or service..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="pl-9 bg-zinc-950/80 text-xs"
            />
          </div>

          <div className="flex items-center gap-2 text-xs text-zinc-400">
            <span className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-zinc-950/80 border border-zinc-800">
              <Globe className="h-3.5 w-3.5 text-sky-400" />
              <span>{ingresses.length} Ingress Routes</span>
            </span>
            <span className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-zinc-950/80 border border-zinc-800">
              <ShieldCheck className="h-3.5 w-3.5 text-emerald-400" />
              <span>{certificates.length} TLS Certificates</span>
            </span>
          </div>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            refetchIngresses()
            refetchCerts()
          }}
          disabled={isFetching}
          className="gap-1.5 text-xs shrink-0"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
          Refresh
        </Button>
      </div>

      {/* Cert-Manager Certificates Grid */}
      {certificates.length > 0 && (
        <div className="space-y-3">
          <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
            <ShieldCheck className="h-4 w-4 text-emerald-400" />
            cert-manager TLS Certificates & Issuers
          </h3>

          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
            {certificates.map((cert) => (
              <div
                key={`${cert.namespace}-${cert.name}`}
                className="p-3.5 rounded-xl border border-zinc-800/80 bg-zinc-900/60 space-y-2"
              >
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <div className="text-sm font-bold text-zinc-100 flex items-center gap-1.5 font-mono">
                      <Lock className="h-3.5 w-3.5 text-emerald-400" />
                      {cert.name}
                    </div>
                    <div className="text-xs text-zinc-400 mt-0.5">{cert.namespace}</div>
                  </div>
                  <Badge variant={cert.isReady ? 'success' : 'warning'} dot className="text-[10px]">
                    {cert.isReady ? 'Issued & Ready' : 'Pending / Verifying'}
                  </Badge>
                </div>

                <div className="text-xs space-y-1 pt-1 border-t border-zinc-800/60 font-mono">
                  {cert.issuer && (
                    <div className="flex justify-between text-zinc-400">
                      <span>Issuer:</span>
                      <span className="text-zinc-200 font-semibold">{cert.issuer}</span>
                    </div>
                  )}
                  {cert.secretName && (
                    <div className="flex justify-between text-zinc-400">
                      <span>Secret:</span>
                      <span className="text-zinc-300">{cert.secretName}</span>
                    </div>
                  )}
                  {cert.notAfter && (
                    <div className="flex justify-between text-zinc-400">
                      <span>Expires:</span>
                      <span className="text-amber-400">
                        {new Date(cert.notAfter).toLocaleDateString()}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Ingress Routes Table */}
      <div className="space-y-3">
        <h3 className="text-xs font-bold text-zinc-300 uppercase tracking-wider flex items-center gap-2">
          <Globe className="h-4 w-4 text-sky-400" />
          NGINX Ingress Routing Table
        </h3>

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
          <div className="overflow-x-auto rounded-xl border border-zinc-800/80 bg-zinc-900/60">
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
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60 font-mono">
                {filteredIngresses.map((ing) => (
                  <tr key={`${ing.namespace}-${ing.name}`} className="hover:bg-zinc-800/40 transition-colors">
                    <td className="p-3 font-semibold text-zinc-100">{ing.name}</td>
                    <td className="p-3 text-amber-300">{ing.namespace}</td>
                    <td className="p-3">
                      {ing.hosts.map((h) => (
                        <div key={h} className="flex items-center gap-1 text-sky-400 hover:underline">
                          <a href={`http://${h}`} target="_blank" rel="noreferrer" className="flex items-center gap-1">
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
                      {ing.paths.map((p, i) => (
                        <div key={i} className="flex items-center gap-1.5 text-emerald-400">
                          <ArrowRight className="h-3 w-3 text-zinc-600" />
                          <span>{p.serviceName}:{p.servicePort}</span>
                        </div>
                      ))}
                    </td>
                    <td className="p-3">
                      {ing.tlsHosts.length > 0 ? (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-950/60 border border-emerald-800 text-emerald-400 text-[10px]">
                          <Lock className="h-2.5 w-2.5" /> TLS
                        </span>
                      ) : (
                        <span className="text-zinc-600">None</span>
                      )}
                    </td>
                    <td className="p-3 text-zinc-400">
                      {ing.ingressClass || 'nginx'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}

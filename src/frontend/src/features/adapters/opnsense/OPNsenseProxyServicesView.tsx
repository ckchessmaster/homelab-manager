import { useState } from 'react'
import {
  Globe,
  Lock,
  RotateCw,
  Search,
  Server,
  AlertCircle,
  ExternalLink,
} from 'lucide-react'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import { Button } from '../../../components/ui/button'
import {
  useOPNsenseHAProxy,
  useOPNsenseAcme,
  useRestartOPNsenseService,
} from './useOPNsense'
import type { OPNsenseService } from '../../../api/opnsense'

interface OPNsenseProxyServicesViewProps {
  instanceId: string
  baseUrl: string
  services?: OPNsenseService[]
}

export function OPNsenseProxyServicesView({
  instanceId,
  baseUrl,
  services = [],
}: OPNsenseProxyServicesViewProps) {
  const { data: haproxy, isLoading: isHaproxyLoading } = useOPNsenseHAProxy(instanceId)
  const { data: acme, isLoading: isAcmeLoading } = useOPNsenseAcme(instanceId)
  const restartServiceMutation = useRestartOPNsenseService(instanceId)

  const [serviceSearch, setServiceSearch] = useState('')

  const handleRestartService = async (service: OPNsenseService) => {
    if (confirm(`Restart service '${service.name}' (${service.description})?`)) {
      await restartServiceMutation.mutateAsync(service.name)
    }
  }

  const filteredServices = services.filter((s) => {
    const q = serviceSearch.toLowerCase()
    return s.name.toLowerCase().includes(q) || s.description.toLowerCase().includes(q)
  })

  return (
    <div className="space-y-6 animate-in fade-in">
      {/* 1. HAProxy Section */}
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Globe className="h-4 w-4 text-orange-400" />
            <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
              HAProxy Ingress & Reverse Proxy
            </h4>
            {haproxy?.isInstalled && (
              <Badge variant={haproxy.running ? 'success' : 'default'} className="text-[10px] py-0">
                {haproxy.running ? 'Proxy Online' : 'Stopped'}
              </Badge>
            )}
          </div>

          <a
            href={`${baseUrl.replace(/\/+$/, '')}/ui/haproxy`}
            target="_blank"
            rel="noreferrer"
            className="text-[11px] text-orange-400 hover:text-orange-300 flex items-center gap-1"
          >
            <span>Configure HAProxy</span>
            <ExternalLink className="h-3 w-3" />
          </a>
        </div>

        {haproxy?.isInstalled ? (
          <div className="space-y-3">
            {/* Frontends Pill Row */}
            {haproxy.frontends && haproxy.frontends.length > 0 && (
              <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-400">
                <span className="text-[11px] font-medium text-zinc-500">Active Frontends:</span>
                {haproxy.frontends.map((fe) => (
                  <Badge key={fe} variant="default" className="text-[11px] font-mono py-0.5">
                    {fe}
                  </Badge>
                ))}
              </div>
            )}

            {/* Backends & Real Servers Table */}
            <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
              <table className="w-full text-left text-xs text-zinc-300">
                <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
                  <tr>
                    <th className="px-4 py-2.5">Backend Pool</th>
                    <th className="px-4 py-2.5">Target Address & Port</th>
                    <th className="px-4 py-2.5">Health State</th>
                    <th className="px-4 py-2.5">Active Sessions</th>
                    <th className="px-4 py-2.5 text-right">Check Latency</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-800/60 font-mono">
                  {haproxy.backends && haproxy.backends.length > 0 ? (
                    haproxy.backends.map((server) => {
                      const isUp = server.status.toUpperCase() === 'UP'
                      return (
                        <tr key={server.name} className="hover:bg-zinc-900/40">
                          <td className="px-4 py-2.5 font-sans font-medium text-zinc-200">
                            {server.name}
                          </td>
                          <td className="px-4 py-2.5 text-zinc-300">
                            {server.address}
                            {server.port ? `:${server.port}` : ''}
                          </td>
                          <td className="px-4 py-2.5">
                            <Badge
                              variant={isUp ? 'success' : 'destructive'}
                              className="text-[10px] py-0"
                            >
                              {server.status}
                            </Badge>
                          </td>
                          <td className="px-4 py-2.5 text-zinc-400">
                            {server.activeSessions ?? 0} active
                          </td>
                          <td className="px-4 py-2.5 text-right text-emerald-400 font-mono">
                            {server.checkDurationMs ? `${server.checkDurationMs} ms` : 'N/A'}
                          </td>
                        </tr>
                      )
                    })
                  ) : (
                    <tr>
                      <td colSpan={5} className="px-4 py-6 text-center text-zinc-500 italic font-sans">
                        No backend servers configured.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        ) : (
          <div className="p-4 rounded-xl border border-zinc-800 bg-zinc-950/40 flex items-center justify-between text-xs text-zinc-400">
            <div className="flex items-center gap-2">
              <AlertCircle className="h-4 w-4 text-zinc-500" />
              <span>
                {isHaproxyLoading
                  ? 'Checking HAProxy plugin status...'
                  : 'HAProxy plugin (os-haproxy) is not installed or enabled on this firewall.'}
              </span>
            </div>
            <a
              href={`${baseUrl.replace(/\/+$/, '')}/ui/core/firmware#plugins`}
              target="_blank"
              rel="noreferrer"
              className="text-orange-400 hover:text-orange-300 inline-flex items-center gap-1"
            >
              <span>View Plugins</span>
              <ExternalLink className="h-3 w-3" />
            </a>
          </div>
        )}
      </div>

      {/* 2. ACME Client Section */}
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Lock className="h-4 w-4 text-emerald-400" />
            <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
              ACME Client & TLS Certificates (Let's Encrypt)
            </h4>
            {acme?.certificates && (
              <Badge variant="default" className="text-[10px] py-0">
                {acme.certificates.length} Certificate{acme.certificates.length !== 1 ? 's' : ''}
              </Badge>
            )}
          </div>

          <a
            href={`${baseUrl.replace(/\/+$/, '')}/ui/acmeclient`}
            target="_blank"
            rel="noreferrer"
            className="text-[11px] text-orange-400 hover:text-orange-300 flex items-center gap-1"
          >
            <span>Manage Certificates</span>
            <ExternalLink className="h-3 w-3" />
          </a>
        </div>

        {acme?.isInstalled ? (
          <div className="overflow-x-auto rounded-xl border border-zinc-800 bg-zinc-950/50">
            <table className="w-full text-left text-xs text-zinc-300">
              <thead className="bg-zinc-900/80 text-zinc-400 font-medium uppercase text-[10px] tracking-wider border-b border-zinc-800">
                <tr>
                  <th className="px-4 py-2.5">Certificate Common Name</th>
                  <th className="px-4 py-2.5">Subject Alt Names (SANs)</th>
                  <th className="px-4 py-2.5">Status</th>
                  <th className="px-4 py-2.5">Expires</th>
                  <th className="px-4 py-2.5 text-right">Days Remaining</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60 font-mono">
                {acme.certificates && acme.certificates.length > 0 ? (
                  acme.certificates.map((cert) => {
                    const days = cert.daysRemaining ?? 30
                    const isExpiringSoon = days <= 15
                    const isExpired = days <= 0

                    return (
                      <tr key={cert.id || cert.name} className="hover:bg-zinc-900/40">
                        <td className="px-4 py-2.5 font-sans font-medium text-zinc-100">
                          <div>
                            <span className="font-mono text-xs">{cert.name}</span>
                            {cert.description && (
                              <p className="text-[11px] text-zinc-500 font-sans">{cert.description}</p>
                            )}
                          </div>
                        </td>

                        <td className="px-4 py-2.5 text-zinc-400 font-mono max-w-xs truncate">
                          {cert.altNames && cert.altNames.length > 0
                            ? cert.altNames.join(', ')
                            : cert.name}
                        </td>

                        <td className="px-4 py-2.5">
                          <Badge
                            variant={isExpired ? 'destructive' : isExpiringSoon ? 'warning' : 'success'}
                            className="text-[10px] py-0"
                          >
                            {cert.status.toUpperCase()}
                          </Badge>
                        </td>

                        <td className="px-4 py-2.5 text-zinc-400">
                          {cert.validTo ? new Date(cert.validTo).toLocaleDateString() : 'N/A'}
                        </td>

                        <td className="px-4 py-2.5 text-right">
                          <span
                            className={`font-bold ${
                              isExpired
                                ? 'text-red-400'
                                : isExpiringSoon
                                ? 'text-amber-400'
                                : 'text-emerald-400'
                            }`}
                          >
                            {days} days
                          </span>
                        </td>
                      </tr>
                    )
                  })
                ) : (
                  <tr>
                    <td colSpan={5} className="px-4 py-6 text-center text-zinc-500 italic font-sans">
                      No ACME certificates issued.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-4 rounded-xl border border-zinc-800 bg-zinc-950/40 flex items-center justify-between text-xs text-zinc-400">
            <div className="flex items-center gap-2">
              <AlertCircle className="h-4 w-4 text-zinc-500" />
              <span>
                {isAcmeLoading
                  ? 'Checking ACME client status...'
                  : 'ACME Client plugin (os-acme-client) is not installed on this firewall.'}
              </span>
            </div>
            <a
              href={`${baseUrl.replace(/\/+$/, '')}/ui/core/firmware#plugins`}
              target="_blank"
              rel="noreferrer"
              className="text-orange-400 hover:text-orange-300 inline-flex items-center gap-1"
            >
              <span>View Plugins</span>
              <ExternalLink className="h-3 w-3" />
            </a>
          </div>
        )}
      </div>

      {/* 3. Core System Services Grid */}
      <div className="space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Server className="h-4 w-4 text-orange-400" />
            <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider">
              Core System Services & Daemons ({services.length})
            </h4>
          </div>

          <div className="relative flex-1 max-w-sm">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-500" />
            <Input
              placeholder="Search services (unbound, wireguard, suricata)..."
              value={serviceSearch}
              onChange={(e) => setServiceSearch(e.target.value)}
              className="pl-8 text-xs h-8 bg-zinc-950/60"
            />
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
          {filteredServices.length > 0 ? (
            filteredServices.map((service) => (
              <div
                key={service.id || service.name}
                className="p-3.5 bg-zinc-950/60 border border-zinc-800/80 rounded-xl flex items-center justify-between gap-3 hover:border-zinc-700/60 transition-colors"
              >
                <div className="min-w-0 flex-1 space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-xs text-zinc-100 truncate font-mono">
                      {service.name}
                    </span>
                    <Badge
                      variant={service.running ? 'success' : 'default'}
                      className="text-[10px] py-0"
                    >
                      {service.running ? 'Running' : 'Stopped'}
                    </Badge>
                  </div>
                  <p className="text-[11px] text-zinc-400 truncate">
                    {service.description || 'OPNsense System Daemon'}
                  </p>
                </div>

                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => handleRestartService(service)}
                  disabled={restartServiceMutation.isPending}
                  className="h-8 px-2.5 text-xs text-zinc-300 hover:text-white hover:bg-zinc-800 shrink-0 gap-1"
                  title="Restart Service"
                >
                  <RotateCw className="h-3 w-3 text-orange-400" />
                  <span>Restart</span>
                </Button>
              </div>
            ))
          ) : (
            <div className="col-span-full text-center p-8 text-zinc-500 text-xs italic">
              No services found matching search query.
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

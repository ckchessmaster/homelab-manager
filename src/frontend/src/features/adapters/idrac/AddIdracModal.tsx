import React, { useState, useEffect } from 'react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Badge } from '../../../components/ui/badge'
import {
  Cpu,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  Radio,
  Save,
  AlertCircle,
  User,
  Lock,
  Eye,
  EyeOff,
  Server,
  Network,
  Terminal,
  Activity,
  Loader2,
  Wrench,
} from 'lucide-react'
import { useSaveIdracInstance, useTestIdracConnection, useInstallIpmiTool } from './useIdrac'
import { useHosts } from '../../hosts/useHosts'
import type { IdracInstanceDto, IdracTestResult } from '../../../api/idrac'

interface AddIdracModalProps {
  open: boolean
  onClose: () => void
  initialInstance?: IdracInstanceDto | null
}

export function AddIdracModal({
  open,
  onClose,
  initialInstance,
}: AddIdracModalProps) {
  const isEditing = Boolean(initialInstance)
  const saveMutation = useSaveIdracInstance()
  const testMutation = useTestIdracConnection()
  const { data: hosts } = useHosts()
  const installIpmiMutation = useInstallIpmiTool()
  const [installingIpmi, setInstallingIpmi] = useState(false)

  // Filter to baremetal / hypervisor nodes only (VMs cannot perform in-band IPMI)
  const baremetalHosts = (hosts || []).filter((h) => {
    const t = (h.targetType || '').toLowerCase()
    return t === 'baremetal' || t === 'proxmox_node' || (!t.includes('vm') && !t.includes('lxc'))
  })

  const [connectionMode, setConnectionMode] = useState<'network' | 'agent'>('agent')
  const [hostId, setHostId] = useState<string>('')
  const [name, setName] = useState('')
  const [id, setId] = useState('')
  const [bmcUrl, setBmcUrl] = useState('')
  const [username, setUsername] = useState('root')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [hostnameOrIp, setHostnameOrIp] = useState('')
  const [allowSelfSignedCert, setAllowSelfSignedCert] = useState(true)

  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<IdracTestResult | null>(null)

  const handleInstallIpmitool = async () => {
    if (!hostId) return
    setInstallingIpmi(true)
    setErrorMessage(null)
    try {
      await installIpmiMutation.mutateAsync(hostId)
      // Auto-retest after installation
      const result = await testMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim() || 'BMC Preflight Test',
        connectionMode: 'agent',
        hostId,
      })
      setTestResult(result)
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to install IPMI tools.')
    } finally {
      setInstallingIpmi(false)
    }
  }

  useEffect(() => {
    if (initialInstance) {
      setConnectionMode(initialInstance.connectionMode === 'agent' ? 'agent' : 'network')
      setHostId(initialInstance.hostId || '')
      setName(initialInstance.name || '')
      setId(initialInstance.id || '')
      setBmcUrl(initialInstance.bmcUrl || '')
      setUsername(initialInstance.username || 'root')
      setPassword('')
      setHostnameOrIp(initialInstance.hostnameOrIp || '')
      setAllowSelfSignedCert(initialInstance.allowSelfSignedCert ?? true)
    } else {
      setConnectionMode('agent')
      setHostId('')
      setName('')
      setId('')
      setBmcUrl('')
      setUsername('root')
      setPassword('')
      setHostnameOrIp('')
      setAllowSelfSignedCert(true)
    }
    setErrorMessage(null)
    setTestResult(null)
  }, [initialInstance, open])

  // When a host is selected in agent mode, auto-populate name if empty
  const handleHostSelect = (selectedId: string) => {
    setHostId(selectedId)
    const selected = hosts?.find((h) => h.id === selectedId)
    if (selected) {
      const displayName = selected.friendlyName || selected.hostname
      if (!name.trim() || name.endsWith('BMC (In-Band IPMI)')) {
        setName(`${displayName} BMC (In-Band IPMI)`)
      }
      setHostnameOrIp(selected.ipAddress)
    }
  }

  const handleTestConnection = async () => {
    setErrorMessage(null)
    setTestResult(null)

    if (connectionMode === 'agent') {
      if (!hostId) {
        setErrorMessage('Please select a baremetal host to test.')
        return
      }

      try {
        const result = await testMutation.mutateAsync({
          id: id.trim() || undefined,
          name: name.trim() || 'BMC Preflight Test',
          connectionMode: 'agent',
          hostId,
        })
        setTestResult(result)
      } catch (err: unknown) {
        setErrorMessage(err instanceof Error ? err.message : 'Connection test failed.')
      }
      return
    }

    if (!bmcUrl.trim()) {
      setErrorMessage('Please provide a BMC URL before testing.')
      return
    }

    if (!username.trim()) {
      setErrorMessage('Please provide a username before testing.')
      return
    }

    if (!isEditing && !password.trim()) {
      setErrorMessage('Please provide a password before testing.')
      return
    }

    try {
      const result = await testMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim() || 'BMC Preflight Test',
        bmcUrl: bmcUrl.trim(),
        username: username.trim(),
        password: password.trim() || undefined,
        hostnameOrIp: hostnameOrIp.trim() || undefined,
        allowSelfSignedCert,
        connectionMode: 'network',
      })
      setTestResult(result)
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Connection test failed.')
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    if (!name.trim()) {
      setErrorMessage('BMC Name is required.')
      return
    }

    if (connectionMode === 'agent') {
      if (!hostId) {
        setErrorMessage('Please select a baremetal host.')
        return
      }

      try {
        await saveMutation.mutateAsync({
          id: id.trim() || undefined,
          name: name.trim(),
          connectionMode: 'agent',
          hostId,
          hostnameOrIp: hostnameOrIp.trim() || undefined,
        })
        onClose()
      } catch (err: unknown) {
        setErrorMessage(err instanceof Error ? err.message : 'Failed to save iDRAC/IPMI configuration.')
      }
      return
    }

    if (!bmcUrl.trim()) {
      setErrorMessage('BMC URL is required.')
      return
    }

    if (!username.trim()) {
      setErrorMessage('Username is required.')
      return
    }

    if (!isEditing && !password.trim()) {
      setErrorMessage('Password is required for new configurations.')
      return
    }

    try {
      await saveMutation.mutateAsync({
        id: id.trim() || undefined,
        name: name.trim(),
        bmcUrl: bmcUrl.trim(),
        username: username.trim(),
        password: password.trim() || undefined,
        hostnameOrIp: hostnameOrIp.trim() || undefined,
        allowSelfSignedCert,
        connectionMode: 'network',
      })
      onClose()
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : 'Failed to save iDRAC configuration.')
    }
  }

  const selectedHost = hosts?.find((h) => h.id === hostId)

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md">
      <DialogHeader onClose={onClose}>
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-purple-500/10 border border-purple-500/20 text-purple-400">
            <Cpu className="h-5 w-5" />
          </div>
          <div>
            <DialogTitle>
              {isEditing ? 'Configure BMC / iDRAC / IPMI Endpoint' : 'Connect BMC / iDRAC / IPMI Endpoint'}
            </DialogTitle>
            <DialogDescription>
              Connect to server hardware management for remote power control, power draw monitoring, and thermal telemetry.
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleSave}>
        <DialogBody className="space-y-4 py-3">
          {errorMessage && (
            <div className="p-3 rounded-lg bg-red-950/40 border border-red-800/60 text-red-300 text-xs flex items-start gap-2.5 animate-in fade-in">
              <AlertCircle className="h-4 w-4 shrink-0 text-red-400 mt-0.5" />
              <div>
                <p className="font-semibold">Configuration Error</p>
                <p className="mt-0.5">{errorMessage}</p>
              </div>
            </div>
          )}

          {/* Connection Mode Selector Tabs */}
          <div className="space-y-1.5">
            <label className="text-xs font-medium text-zinc-300">
              Connection Architecture
            </label>
            <div className="grid grid-cols-2 gap-2 p-1 bg-zinc-950/60 border border-zinc-800 rounded-lg">
              <button
                type="button"
                onClick={() => setConnectionMode('agent')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-md text-xs font-medium transition-all ${
                  connectionMode === 'agent'
                    ? 'bg-purple-600/90 text-white shadow-xs font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/50'
                }`}
              >
                <Terminal className="h-3.5 w-3.5 text-purple-200" />
                <span>Baremetal Host Agent (Local IPMI)</span>
              </button>
              <button
                type="button"
                onClick={() => setConnectionMode('network')}
                className={`flex items-center justify-center gap-2 py-2 px-3 rounded-md text-xs font-medium transition-all ${
                  connectionMode === 'network'
                    ? 'bg-purple-600/90 text-white shadow-xs font-semibold'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/50'
                }`}
              >
                <Network className="h-3.5 w-3.5 text-purple-200" />
                <span>Direct Network (Redfish / IP)</span>
              </button>
            </div>
          </div>

          {/* Test Result Callout */}
          {testResult && (
            <div
              className={`p-3 rounded-lg border text-xs animate-in fade-in ${
                testResult.success
                  ? 'bg-emerald-950/30 border-emerald-800/60 text-emerald-300'
                  : 'bg-red-950/30 border-red-800/60 text-red-300'
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2 font-medium">
                  {testResult.success ? (
                    <CheckCircle2 className="h-4 w-4 text-emerald-400 shrink-0" />
                  ) : (
                    <XCircle className="h-4 w-4 text-red-400 shrink-0" />
                  )}
                  <span>
                    {testResult.success ? 'BMC Connection Successful' : 'Connection Failed'}
                  </span>
                </div>
                <Badge variant={testResult.success ? 'success' : 'destructive'} className="text-[10px]">
                  {testResult.latencyMs}ms
                </Badge>
              </div>

              {testResult.success ? (
                <div className="mt-2 grid grid-cols-2 gap-2 text-[11px] text-zinc-300 bg-zinc-950/40 p-2 rounded border border-zinc-800/60">
                  <div>
                    <span className="text-zinc-500">Model:</span>{' '}
                    <span className="font-semibold text-zinc-200">{testResult.model || 'Unknown'}</span>
                  </div>
                  <div>
                    <span className="text-zinc-500">Power State:</span>{' '}
                    <Badge variant={testResult.powerState?.toLowerCase() === 'on' ? 'success' : 'destructive'} className="text-[10px] ml-1">
                      {testResult.powerState || 'Unknown'}
                    </Badge>
                  </div>
                  <div>
                    <span className="text-zinc-500">BIOS / Firmware:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.biosVersion || 'N/A'}</span>
                  </div>
                  <div>
                    <span className="text-zinc-500">Service Tag / Serial:</span>{' '}
                    <span className="font-mono text-zinc-200">{testResult.serialNumber || 'N/A'}</span>
                  </div>
                </div>
              ) : (
                <div className="mt-1.5 space-y-2">
                  <p className="text-red-400 font-mono text-[11px] break-all">
                    {testResult.message || 'Unable to reach BMC endpoint.'}
                  </p>
                  {connectionMode === 'agent' && hostId && testResult.message?.toLowerCase().includes('ipmitool') && (
                    <div className="pt-2 border-t border-red-900/40 flex items-center justify-between gap-2">
                      <span className="text-[11px] text-zinc-300">
                        Missing IPMI utilities on host. The agent can install them automatically.
                      </span>
                      <Button
                        type="button"
                        size="sm"
                        onClick={handleInstallIpmitool}
                        disabled={installingIpmi || !selectedHost?.agent?.isOnline}
                        className="bg-purple-600 hover:bg-purple-500 text-white text-xs h-7 px-2.5 shrink-0"
                      >
                        {installingIpmi ? (
                          <>
                            <Loader2 className="h-3.5 w-3.5 mr-1.5 animate-spin" />
                            Installing...
                          </>
                        ) : (
                          <>
                            <Wrench className="h-3.5 w-3.5 mr-1.5" />
                            Install IPMI Tools
                          </>
                        )}
                      </Button>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* Identity Fields */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <Server className="h-3.5 w-3.5 text-zinc-400" />
                Server / BMC Name
              </label>
              <Input
                placeholder={connectionMode === 'agent' ? 'e.g. Proxmox Node 1 BMC' : 'e.g. Dell PowerEdge R730xd'}
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-medium text-zinc-300">
                Instance Identifier (Slug)
              </label>
              <Input
                placeholder="Auto-generated if blank"
                value={id}
                onChange={(e) => setId(e.target.value)}
                disabled={isEditing}
                className="font-mono text-xs"
              />
            </div>
          </div>

          {/* AGENT MODE: Baremetal Host Selector */}
          {connectionMode === 'agent' && (
            <div className="space-y-3 p-3 bg-zinc-950/40 border border-zinc-800 rounded-lg">
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                  <span className="flex items-center gap-1.5">
                    <Server className="h-3.5 w-3.5 text-purple-400" />
                    Select Baremetal Host System
                  </span>
                  <span className="text-[10px] text-zinc-500">e.g. Proxmox PVE, Debian host</span>
                </label>

                <select
                  value={hostId}
                  onChange={(e) => handleHostSelect(e.target.value)}
                  className="w-full bg-zinc-900/90 border border-zinc-700/80 rounded-md text-xs text-zinc-200 p-2 focus:ring-1 focus:ring-purple-500 focus:border-purple-500 outline-hidden font-mono"
                  required
                >
                  <option value="">
                    {baremetalHosts.length === 0
                      ? '-- No Baremetal Hosts Available --'
                      : `-- Choose Baremetal Host (${baremetalHosts.length} available) --`}
                  </option>
                  {baremetalHosts.map((h) => (
                    <option key={h.id} value={h.id}>
                      {h.friendlyName ? `${h.friendlyName} (${h.hostname})` : h.hostname} — {h.ipAddress} [Baremetal: {h.targetType || 'host'}] {h.agent?.isOnline ? '● Online' : '○ Offline'}
                    </option>
                  ))}
                </select>

                {baremetalHosts.length === 0 && (
                  <p className="text-[11px] text-amber-400 mt-1 flex items-center gap-1.5">
                    <AlertCircle className="h-3.5 w-3.5 shrink-0 text-amber-400" />
                    No baremetal hosts detected. Virtual machines (VMs) do not possess physical BMC hardware and cannot be used for in-band IPMI.
                  </p>
                )}
              </div>

              {selectedHost && (
                <div className="p-2.5 bg-zinc-900/60 rounded border border-zinc-800/80 text-xs flex items-center justify-between">
                  <div className="space-y-0.5">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-zinc-200">
                        {selectedHost.friendlyName || selectedHost.hostname}
                      </span>
                      <Badge variant="outline" className="text-[10px] py-0 font-mono">
                        {selectedHost.targetType}
                      </Badge>
                    </div>
                    <p className="text-[11px] text-zinc-400 font-mono">
                      IP: {selectedHost.ipAddress} • OS: {selectedHost.osFamily || 'Linux'}
                    </p>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <Activity className={`h-3 w-3 ${selectedHost.agent?.isOnline ? 'text-emerald-400' : 'text-zinc-500'}`} />
                    <span className={`text-[11px] font-medium ${selectedHost.agent?.isOnline ? 'text-emerald-400' : 'text-zinc-400'}`}>
                      {selectedHost.agent?.isOnline ? 'Agent Connected' : 'Agent Offline'}
                    </span>
                  </div>
                </div>
              )}

              <p className="text-[11px] text-zinc-400 leading-relaxed">
                Commands will execute in-band using Linux OpenIPMI (<code className="text-purple-300 font-mono">ipmitool</code>) directly on this host. No dedicated BMC IP or passwords required.
              </p>
            </div>
          )}

          {/* NETWORK MODE: URL, Credentials, Self-Signed */}
          {connectionMode === 'network' && (
            <>
              {/* BMC URL */}
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                  <span>BMC Endpoint URL or IP</span>
                  <span className="text-[10px] text-zinc-500 font-mono">https://&lt;ip&gt; or ipmi://&lt;ip&gt;</span>
                </label>
                <Input
                  placeholder="https://192.168.1.9 or 192.168.1.9"
                  value={bmcUrl}
                  onChange={(e) => setBmcUrl(e.target.value)}
                  className="font-mono text-xs"
                  required
                />
                <p className="text-[10px] text-zinc-400">
                  Supports modern Redfish (HTTPS) with automatic IPMI-over-LAN (UDP 623 / RMCP+) fallback for legacy servers like Dell iDRAC 7 (R720/R720xd).
                </p>
              </div>

              {/* Associated Host IP / Correlated Host */}
              <div className="space-y-1.5">
                <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                  <span className="flex items-center gap-1.5">
                    <Network className="h-3.5 w-3.5 text-zinc-400" />
                    Linked Host IP / BMC IP
                  </span>
                  <span className="text-[10px] text-zinc-500">Links to Inventory</span>
                </label>
                <Input
                  placeholder="192.168.1.120 (for Host matching)"
                  value={hostnameOrIp}
                  onChange={(e) => setHostnameOrIp(e.target.value)}
                  className="font-mono text-xs"
                />
              </div>

              {/* Credentials */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pt-2 border-t border-zinc-800/80">
                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                    <User className="h-3.5 w-3.5 text-zinc-400" />
                    BMC Username
                  </label>
                  <Input
                    placeholder="root"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    className="font-mono text-xs"
                    required
                  />
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-medium text-zinc-300 flex items-center justify-between">
                    <span className="flex items-center gap-1.5">
                      <Lock className="h-3.5 w-3.5 text-zinc-400" />
                      Password
                    </span>
                    {isEditing && (
                      <span className="text-[10px] text-zinc-500">Keep existing</span>
                    )}
                  </label>
                  <div className="relative">
                    <Input
                      type={showPassword ? 'text' : 'password'}
                      placeholder={isEditing ? '••••••••••••••••' : 'Enter password...'}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      className="font-mono text-xs pr-9"
                      required={!isEditing}
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300"
                    >
                      {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>
              </div>

              {/* TLS Options */}
              <div className="pt-2 border-t border-zinc-800/80">
                <label className="flex items-center gap-2.5 cursor-pointer text-xs text-zinc-300">
                  <input
                    type="checkbox"
                    checked={allowSelfSignedCert}
                    onChange={(e) => setAllowSelfSignedCert(e.target.checked)}
                    className="rounded bg-zinc-900 border-zinc-700 text-purple-500 focus:ring-purple-500/20"
                  />
                  <span className="flex items-center gap-1.5">
                    <ShieldCheck className="h-4 w-4 text-purple-400" />
                    Allow Self-Signed TLS Certificates (Common for out-of-band BMCs)
                  </span>
                </label>
              </div>
            </>
          )}
        </DialogBody>

        <DialogFooter className="flex items-center justify-between sm:justify-between w-full">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleTestConnection}
            disabled={testMutation.isPending || (connectionMode === 'agent' ? !hostId : (!bmcUrl.trim() || !username.trim()))}
            className="gap-1.5 border-purple-500/30 hover:bg-purple-950/30 text-purple-300"
          >
            <Radio className={`h-3.5 w-3.5 ${testMutation.isPending ? 'animate-pulse text-purple-400' : ''}`} />
            {testMutation.isPending ? 'Testing...' : 'Test Preflight'}
          </Button>

          <div className="flex items-center gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              size="sm"
              disabled={saveMutation.isPending}
              className="gap-1.5 bg-purple-600 hover:bg-purple-500 text-white font-semibold"
            >
              <Save className="h-3.5 w-3.5" />
              {saveMutation.isPending ? 'Saving...' : isEditing ? 'Save Changes' : 'Connect BMC'}
            </Button>
          </div>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

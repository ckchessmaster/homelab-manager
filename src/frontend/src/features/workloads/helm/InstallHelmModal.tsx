import React, { useState, useEffect } from 'react'
import {
  Package,
  Loader2,
  AlertTriangle,
  CheckCircle2,
  Sliders,
  FileCode,
  Globe,
  Tag,
  KeyRound,
} from 'lucide-react'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../../components/ui/dialog'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { useInstallHelmRelease } from './useHelm'
import { CreateResourceModal } from '../CreateResourceModal'
import type { InstallHelmReleasePayload } from '../../../api/helm'

interface InstallHelmModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  availableNamespaces?: string[]
  initialData?: Partial<InstallHelmReleasePayload> | null
  onSuccess?: (releaseName: string) => void
}

export function InstallHelmModal({
  open,
  onClose,
  clusterId,
  availableNamespaces = [],
  initialData,
  onSuccess,
}: InstallHelmModalProps) {
  const [releaseName, setReleaseName] = useState('')
  const [namespace, setNamespace] = useState('default')
  const [chartName, setChartName] = useState('')
  const [repoUrl, setRepoUrl] = useState('')
  const [version, setVersion] = useState('')
  const [valuesYaml, setValuesYaml] = useState('')
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [wait, setWait] = useState(false)
  const [timeoutSeconds, setTimeoutSeconds] = useState(300)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [isCreateSecretOpen, setIsCreateSecretOpen] = useState(false)

  const installMutation = useInstallHelmRelease(clusterId)

  useEffect(() => {
    if (open) {
      const initRel = initialData?.releaseName || ''
      const initNs = initialData?.namespace || availableNamespaces[0] || 'default'
      const initChart = initialData?.chartName || ''
      const fallbackRepo =
        initialData?.repoUrl ||
        localStorage.getItem(`helm_repo_${clusterId}_${initNs}_${initRel}`) ||
        localStorage.getItem(`helm_chart_repo_${initChart}`) ||
        ''

      setReleaseName(initRel)
      setNamespace(initNs)
      setChartName(initChart)
      setRepoUrl(fallbackRepo)
      setVersion(initialData?.version || '')
      setValuesYaml(initialData?.valuesYaml || '')
      setWait(initialData?.wait || false)
      setTimeoutSeconds(initialData?.timeoutSeconds || 300)
      setErrorMessage(null)
      setShowAdvanced(false)
    }
  }, [open, initialData, availableNamespaces, clusterId])

  const trimmedRelease = releaseName.trim()
  const trimmedChart = chartName.trim()
  const trimmedNs = namespace.trim()
  const isValid = trimmedRelease.length > 0 && trimmedChart.length > 0 && trimmedNs.length > 0

  const handleInstall = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!isValid || installMutation.isPending) return
    setErrorMessage(null)

    try {
      const result = await installMutation.mutateAsync({
        releaseName: trimmedRelease,
        namespace: trimmedNs,
        chartName: trimmedChart,
        repoUrl: repoUrl.trim() || undefined,
        version: version.trim() || undefined,
        valuesYaml: valuesYaml.trim() || undefined,
        createNamespace: true,
        wait,
        timeoutSeconds,
      })

      if (result.success) {
        if (repoUrl.trim()) {
          localStorage.setItem(`helm_repo_${clusterId}_${trimmedNs}_${trimmedRelease}`, repoUrl.trim())
          localStorage.setItem(`helm_chart_repo_${trimmedChart}`, repoUrl.trim())
        }
        onSuccess?.(trimmedRelease)
        onClose()
      } else {
        setErrorMessage(result.message || 'Helm chart deployment failed.')
      }
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to deploy Helm chart.')
    }
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="xl">
      <DialogHeader>
        <div className="flex items-center gap-2">
          <div className="p-2 rounded-lg bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
            <Package className="w-5 h-5" />
          </div>
          <div>
            <DialogTitle>Deploy Helm Release</DialogTitle>
            <p className="text-xs text-zinc-400">
              Install or upgrade a Helm chart on cluster <span className="font-semibold text-zinc-300">{clusterId}</span>
            </p>
          </div>
        </div>
      </DialogHeader>

      <form onSubmit={handleInstall}>
        <DialogBody className="space-y-4 max-h-[70vh] overflow-y-auto pr-1">
          {errorMessage && (
            <div className="p-3 bg-red-950/40 border border-red-800/60 rounded-lg space-y-2 text-xs text-red-300">
              <div className="flex items-start gap-2">
                <AlertTriangle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
                <div className="font-semibold text-red-200">Helm Deployment Failed</div>
              </div>
              <div className="font-mono text-[11px] bg-black/40 p-2.5 rounded border border-red-900/50 whitespace-pre-wrap break-all max-h-48 overflow-y-auto text-red-300">
                {errorMessage}
              </div>
              {(errorMessage.includes('DeadlineExceeded') || errorMessage.includes('failed pre-install')) && (
                <div className="text-[11px] text-zinc-400 bg-zinc-900/80 p-2.5 rounded border border-zinc-800 space-y-1">
                  <div className="font-medium text-amber-400 flex items-center gap-1.5">
                    <span>💡 Troubleshooting Hook Job Failures:</span>
                  </div>
                  <ul className="list-disc list-inside space-y-0.5 text-zinc-300">
                    <li>Confirm database connectivity & credentials in target namespace <code className="text-zinc-200 font-mono">{namespace}</code></li>
                    <li>Ensure required secrets (masterkey, db credentials) are properly mapped in Custom Values</li>
                    <li>Before retrying, delete the failed job: <code className="text-indigo-300 font-mono select-all">kubectl delete job &lt;job-name&gt; -n {namespace}</code></li>
                  </ul>
                </div>
              )}
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Release Name */}
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Release Name <span className="text-red-400">*</span>
              </label>
              <Input
                placeholder="e.g. ingress-nginx"
                value={releaseName}
                onChange={(e) => setReleaseName(e.target.value)}
                required
                className="bg-zinc-900 border-zinc-700 text-zinc-100"
              />
            </div>

            {/* Target Namespace */}
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Target Namespace <span className="text-red-400">*</span>
              </label>
              <Input
                placeholder="e.g. default, ingress-nginx"
                value={namespace}
                onChange={(e) => setNamespace(e.target.value)}
                required
                className="bg-zinc-900 border-zinc-700 text-zinc-100"
              />
              <p className="text-[10px] text-zinc-500 mt-1">Created automatically if not already present.</p>
            </div>
          </div>

          {/* Prerequisite Helper */}
          <div className="flex items-center justify-between p-2.5 rounded-lg bg-purple-950/20 border border-purple-800/40 text-xs">
            <div className="flex items-center gap-2 text-purple-300">
              <KeyRound className="w-3.5 h-3.5 shrink-0" />
              <span>Need prerequisite secrets (database passwords, API tokens, TLS certs)?</span>
            </div>
            <button
              type="button"
              onClick={() => setIsCreateSecretOpen(true)}
              className="text-[11px] font-semibold text-purple-300 hover:text-white px-2 py-0.5 rounded bg-purple-950/50 hover:bg-purple-900/60 border border-purple-700/60 transition-colors cursor-pointer"
            >
              + Create Secret
            </button>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* Chart Name */}
            <div className="md:col-span-2">
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Chart Name / Reference <span className="text-red-400">*</span>
              </label>
              <div className="relative">
                <Input
                  placeholder="e.g. ingress-nginx, cert-manager"
                  value={chartName}
                  onChange={(e) => setChartName(e.target.value)}
                  required
                  className="bg-zinc-900 border-zinc-700 text-zinc-100 pl-8"
                />
                <Tag className="w-3.5 h-3.5 text-zinc-400 absolute left-2.5 top-3" />
              </div>
            </div>

            {/* Version */}
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Chart Version <span className="text-zinc-500">(optional)</span>
              </label>
              <Input
                placeholder="Latest"
                value={version}
                onChange={(e) => setVersion(e.target.value)}
                className="bg-zinc-900 border-zinc-700 text-zinc-100"
              />
            </div>
          </div>

          {/* Repository URL */}
          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1.5">
              Repository URL <span className="text-zinc-500">(optional if using OCI or packaged chart)</span>
            </label>
            <div className="relative">
              <Input
                placeholder="e.g. https://kubernetes.github.io/ingress-nginx"
                value={repoUrl}
                onChange={(e) => setRepoUrl(e.target.value)}
                className="bg-zinc-900 border-zinc-700 text-zinc-100 pl-8"
              />
              <Globe className="w-3.5 h-3.5 text-zinc-400 absolute left-2.5 top-3" />
            </div>
          </div>

          {/* Custom Values YAML */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <FileCode className="w-3.5 h-3.5 text-indigo-400" />
                Custom Values (YAML)
              </label>
              <span className="text-[10px] text-zinc-500">Overrides default chart values</span>
            </div>
            <textarea
              rows={8}
              placeholder={`# Custom values override\ncontroller:\n  service:\n    type: LoadBalancer`}
              value={valuesYaml}
              onChange={(e) => setValuesYaml(e.target.value)}
              className="w-full bg-zinc-950 font-mono text-xs text-zinc-200 border border-zinc-800 rounded-md p-3 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {/* Advanced Accordion */}
          <div className="pt-2">
            <button
              type="button"
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="text-xs text-indigo-400 hover:text-indigo-300 flex items-center gap-1 font-medium transition-colors"
            >
              <Sliders className="w-3.5 h-3.5" />
              {showAdvanced ? 'Hide Advanced Options' : 'Show Advanced Options'}
            </button>

            {showAdvanced && (
              <div className="mt-3 p-3 bg-zinc-900/60 border border-zinc-800 rounded-lg space-y-3">
                <div className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    id="helm-wait-checkbox"
                    checked={wait}
                    onChange={(e) => setWait(e.target.checked)}
                    className="rounded border-zinc-700 bg-zinc-900 text-indigo-600 focus:ring-indigo-500 w-4 h-4"
                  />
                  <label htmlFor="helm-wait-checkbox" className="text-xs text-zinc-300 cursor-pointer">
                    Wait for all Pods and PVCs to become Ready before completing (<code className="text-[11px] text-zinc-400">--wait</code>)
                  </label>
                </div>

                <div className="flex items-center gap-3">
                  <label className="text-xs text-zinc-300 whitespace-nowrap">
                    Operation Timeout:
                  </label>
                  <Input
                    type="number"
                    min={30}
                    max={1800}
                    value={timeoutSeconds}
                    onChange={(e) => setTimeoutSeconds(parseInt(e.target.value) || 300)}
                    className="w-24 bg-zinc-900 border-zinc-700 text-zinc-100 text-xs"
                  />
                  <span className="text-xs text-zinc-500">seconds</span>
                </div>
              </div>
            )}
          </div>
        </DialogBody>

        <DialogFooter className="flex items-center justify-between">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>

          <Button
            type="submit"
            disabled={!isValid || installMutation.isPending}
            className="bg-indigo-600 hover:bg-indigo-500 text-white flex items-center gap-1.5"
          >
            {installMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                Deploying Chart...
              </>
            ) : (
              <>
                <CheckCircle2 className="w-4 h-4" />
                Deploy Release
              </>
            )}
          </Button>
        </DialogFooter>
      </form>
    </Dialog>

    <CreateResourceModal
      open={isCreateSecretOpen}
      onClose={() => setIsCreateSecretOpen(false)}
      initialClusterId={clusterId}
      initialNamespace={namespace || 'default'}
      initialTab="secret"
      availableNamespaces={availableNamespaces}
    />
  </>
  )
}

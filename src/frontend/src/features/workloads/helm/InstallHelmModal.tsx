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
  RefreshCw,
  History,
  BookOpen,
  Lock,
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
import { useInstallHelmRelease, useHelmCatalog } from './useHelm'
import { CreateResourceModal } from '../CreateResourceModal'
import { getHelmReleaseDetail, type InstallHelmReleasePayload } from '../../../api/helm'

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
  const [reuseValues, setReuseValues] = useState(false)
  const [resetValues, setResetValues] = useState(false)
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [wait, setWait] = useState(false)
  const [timeoutSeconds, setTimeoutSeconds] = useState(300)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [infoMessage, setInfoMessage] = useState<string | null>(null)
  const [isReloadingLiveValues, setIsReloadingLiveValues] = useState(false)
  const [isCreateSecretOpen, setIsCreateSecretOpen] = useState(false)

  const isUpgrade = Boolean(initialData?.releaseName && initialData?.chartName)
  const { data: catalog = [] } = useHelmCatalog()
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
      setWait(initialData?.wait || false)
      setTimeoutSeconds(initialData?.timeoutSeconds || 300)
      setErrorMessage(null)
      setInfoMessage(null)
      setShowAdvanced(false)

      const initialValues = initialData?.valuesYaml || ''
      setValuesYaml(initialValues)
      setReuseValues(initialData?.reuseValues ?? (!initialValues && Boolean(initRel)))
      setResetValues(initialData?.resetValues ?? false)

      // If opening an upgrade without values, attempt local storage backup or fetch live from cluster
      if (initRel && initNs && !initialValues) {
        const cachedBackup = localStorage.getItem(`helm_values_${clusterId}_${initNs}_${initRel}`)
        if (cachedBackup) {
          setValuesYaml(cachedBackup)
          setInfoMessage('Loaded configuration values from previous local session backup.')
        } else {
          getHelmReleaseDetail(clusterId, initNs, initRel)
            .then((detail) => {
              if (detail.valuesYaml) {
                setValuesYaml(detail.valuesYaml)
                setInfoMessage('Loaded live custom values from cluster.')
              } else if (detail.computedValuesYaml) {
                setInfoMessage('Release has no custom overrides; running on chart defaults.')
              }
            })
            .catch(() => {})
        }
      }
    }
  }, [open, initialData, availableNamespaces, clusterId])

  const trimmedRelease = releaseName.trim()
  const trimmedChart = chartName.trim()
  const trimmedNs = namespace.trim()
  const isValid = trimmedRelease.length > 0 && trimmedChart.length > 0 && trimmedNs.length > 0

  const localBackupKey = `helm_values_${clusterId}_${trimmedNs}_${trimmedRelease}`
  const hasLocalBackup = Boolean(trimmedRelease && trimmedNs && localStorage.getItem(localBackupKey))

  const matchingCatalogItem = catalog.find(
    (c) =>
      c.chartName.toLowerCase() === trimmedChart.toLowerCase() ||
      c.id.toLowerCase() === trimmedChart.toLowerCase() ||
      (c.repoUrl && repoUrl.trim() && c.repoUrl.toLowerCase() === repoUrl.trim().toLowerCase())
  )

  const handleReloadLiveValues = async () => {
    if (!trimmedRelease || !trimmedNs) return
    setIsReloadingLiveValues(true)
    setInfoMessage(null)
    setErrorMessage(null)
    try {
      const detail = await getHelmReleaseDetail(clusterId, trimmedNs, trimmedRelease)
      if (detail.valuesYaml) {
        setValuesYaml(detail.valuesYaml)
        setInfoMessage('Reloaded live custom values from cluster.')
      } else if (detail.computedValuesYaml) {
        setValuesYaml(detail.computedValuesYaml)
        setInfoMessage('No custom values set; loaded full computed values from cluster.')
      } else {
        setInfoMessage('No values found on cluster for this release.')
      }
    } catch (err: any) {
      setErrorMessage(err?.message || 'Failed to fetch live values from cluster.')
    } finally {
      setIsReloadingLiveValues(false)
    }
  }

  const handleRestoreFromBackup = () => {
    const backup = localStorage.getItem(localBackupKey)
    if (backup) {
      setValuesYaml(backup)
      setInfoMessage('Restored values from previous deployment session.')
    }
  }

  const handleLoadCatalogTemplate = () => {
    if (matchingCatalogItem?.defaultValuesYaml) {
      setValuesYaml(matchingCatalogItem.defaultValuesYaml)
      setInfoMessage(`Loaded default values template from ${matchingCatalogItem.name}.`)
    }
  }

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
        reuseValues: isUpgrade ? reuseValues : undefined,
        resetValues: isUpgrade ? resetValues : undefined,
        createNamespace: true,
        wait,
        timeoutSeconds,
      })

      if (result.success) {
        if (repoUrl.trim()) {
          localStorage.setItem(`helm_repo_${clusterId}_${trimmedNs}_${trimmedRelease}`, repoUrl.trim())
          localStorage.setItem(`helm_chart_repo_${trimmedChart}`, repoUrl.trim())
        }
        if (valuesYaml.trim()) {
          localStorage.setItem(localBackupKey, valuesYaml.trim())
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
            <DialogTitle>
              {isUpgrade ? `Configure / Upgrade Release: ${releaseName}` : 'Deploy Helm Release'}
            </DialogTitle>
            <p className="text-xs text-zinc-400">
              {isUpgrade
                ? `Update configuration or upgrade Helm chart in namespace '${namespace}' on cluster `
                : 'Install or upgrade a Helm chart on cluster '}
              <span className="font-semibold text-zinc-300">{clusterId}</span>
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
              <label className="text-xs font-medium text-zinc-300 mb-1.5 flex items-center justify-between">
                <span>
                  Release Name <span className="text-red-400">*</span>
                </span>
                {isUpgrade && (
                  <span className="text-[10px] text-zinc-500 flex items-center gap-1 font-normal">
                    <Lock className="w-2.5 h-2.5" /> Locked
                  </span>
                )}
              </label>
              <Input
                placeholder="e.g. ingress-nginx"
                value={releaseName}
                onChange={(e) => setReleaseName(e.target.value)}
                required
                disabled={isUpgrade}
                className={`bg-zinc-900 border-zinc-700 text-zinc-100 ${
                  isUpgrade ? 'opacity-70 cursor-not-allowed bg-zinc-900/60' : ''
                }`}
              />
            </div>

            {/* Target Namespace */}
            <div>
              <label className="text-xs font-medium text-zinc-300 mb-1.5 flex items-center justify-between">
                <span>
                  Target Namespace <span className="text-red-400">*</span>
                </span>
                {isUpgrade && (
                  <span className="text-[10px] text-zinc-500 flex items-center gap-1 font-normal">
                    <Lock className="w-2.5 h-2.5" /> Locked
                  </span>
                )}
              </label>
              <Input
                placeholder="e.g. default, ingress-nginx"
                value={namespace}
                onChange={(e) => setNamespace(e.target.value)}
                required
                disabled={isUpgrade}
                className={`bg-zinc-900 border-zinc-700 text-zinc-100 ${
                  isUpgrade ? 'opacity-70 cursor-not-allowed bg-zinc-900/60' : ''
                }`}
              />
              {!isUpgrade && (
                <p className="text-[10px] text-zinc-500 mt-1">Created automatically if not already present.</p>
              )}
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
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5 mb-1.5">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <FileCode className="w-3.5 h-3.5 text-indigo-400" />
                Custom Values (YAML)
              </label>

              {/* Action Toolbar */}
              <div className="flex items-center gap-1.5 flex-wrap">
                {isUpgrade && (
                  <button
                    type="button"
                    disabled={isReloadingLiveValues}
                    onClick={handleReloadLiveValues}
                    className="text-[11px] text-indigo-400 hover:text-indigo-300 bg-indigo-950/40 hover:bg-indigo-950/70 border border-indigo-800/50 px-2 py-0.5 rounded flex items-center gap-1 transition-colors cursor-pointer disabled:opacity-50"
                    title="Fetch current live configuration values directly from cluster"
                  >
                    <RefreshCw className={`w-3 h-3 ${isReloadingLiveValues ? 'animate-spin' : ''}`} />
                    <span>Reload Live</span>
                  </button>
                )}

                {hasLocalBackup && (
                  <button
                    type="button"
                    onClick={handleRestoreFromBackup}
                    className="text-[11px] text-amber-400 hover:text-amber-300 bg-amber-950/40 hover:bg-amber-950/70 border border-amber-800/50 px-2 py-0.5 rounded flex items-center gap-1 transition-colors cursor-pointer"
                    title="Restore values from previous session saved in browser storage"
                  >
                    <History className="w-3 h-3" />
                    <span>Restore Backup</span>
                  </button>
                )}

                {matchingCatalogItem?.defaultValuesYaml && (
                  <button
                    type="button"
                    onClick={handleLoadCatalogTemplate}
                    className="text-[11px] text-zinc-400 hover:text-zinc-200 bg-zinc-800/60 hover:bg-zinc-800 border border-zinc-700 px-2 py-0.5 rounded flex items-center gap-1 transition-colors cursor-pointer"
                    title="Load starter configuration template from catalog"
                  >
                    <BookOpen className="w-3 h-3" />
                    <span>Load Template</span>
                  </button>
                )}

                {valuesYaml && (
                  <button
                    type="button"
                    onClick={() => setValuesYaml('')}
                    className="text-[11px] text-zinc-500 hover:text-zinc-300 px-1.5 py-0.5"
                    title="Clear values text"
                  >
                    Clear
                  </button>
                )}
              </div>
            </div>

            {infoMessage && (
              <div className="mb-2 p-2 rounded bg-indigo-950/30 border border-indigo-800/40 text-[11px] text-indigo-300 flex items-center justify-between">
                <span>{infoMessage}</span>
                <button
                  type="button"
                  onClick={() => setInfoMessage(null)}
                  className="text-zinc-400 hover:text-zinc-200 text-xs px-1"
                >
                  ✕
                </button>
              </div>
            )}

            <textarea
              rows={8}
              placeholder={`# Custom values override\ncontroller:\n  service:\n    type: LoadBalancer`}
              value={valuesYaml}
              onChange={(e) => {
                setValuesYaml(e.target.value)
                if (infoMessage) setInfoMessage(null)
              }}
              className="w-full bg-zinc-950 font-mono text-xs text-zinc-200 border border-zinc-800 rounded-md p-3 focus:outline-none focus:ring-1 focus:ring-indigo-500 leading-relaxed"
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
                {isUpgrade && (
                  <>
                    <div className="flex items-start gap-2">
                      <input
                        type="checkbox"
                        id="helm-reuse-values"
                        checked={reuseValues}
                        onChange={(e) => {
                          setReuseValues(e.target.checked)
                          if (e.target.checked) setResetValues(false)
                        }}
                        className="rounded border-zinc-700 bg-zinc-900 text-indigo-600 focus:ring-indigo-500 w-4 h-4 mt-0.5"
                      />
                      <div>
                        <label htmlFor="helm-reuse-values" className="text-xs text-zinc-300 cursor-pointer font-medium">
                          Reuse existing release values (<code className="text-[11px] text-zinc-400">--reuse-values</code>)
                        </label>
                        <p className="text-[11px] text-zinc-500">
                          Preserves previously configured values from the current release and merges your overrides on top.
                        </p>
                      </div>
                    </div>

                    <div className="flex items-start gap-2">
                      <input
                        type="checkbox"
                        id="helm-reset-values"
                        checked={resetValues}
                        onChange={(e) => {
                          setResetValues(e.target.checked)
                          if (e.target.checked) setReuseValues(false)
                        }}
                        className="rounded border-zinc-700 bg-zinc-900 text-indigo-600 focus:ring-indigo-500 w-4 h-4 mt-0.5"
                      />
                      <div>
                        <label htmlFor="helm-reset-values" className="text-xs text-zinc-300 cursor-pointer font-medium">
                          Reset values to chart defaults (<code className="text-[11px] text-zinc-400">--reset-values</code>)
                        </label>
                        <p className="text-[11px] text-zinc-500">
                          Resets any previously stored values to the default values present in the chart.
                        </p>
                      </div>
                    </div>
                  </>
                )}

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
                {isUpgrade ? 'Upgrading Release...' : 'Deploying Chart...'}
              </>
            ) : (
              <>
                <CheckCircle2 className="w-4 h-4" />
                {isUpgrade ? 'Save & Upgrade Release' : 'Deploy Release'}
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

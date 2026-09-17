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
  KeyRound,
  FileText,
  Plus,
  Trash2,
  Eye,
  EyeOff,
  AlignLeft,
  Link2,
  Loader2,
  AlertTriangle,
  Sparkles,
} from 'lucide-react'
import {
  useSecretDetail,
  useUpdateSecret,
  useConfigMapDetail,
  useUpdateConfigMap,
} from './useKubernetesResources'
import {
  isConnectionString,
  hasUnencodedUriPassword,
  encodeUriPassword,
  decodeUriPassword,
  autoEncodeSecretForSubmission,
} from '../../utils/urlEncoding'

interface KeyValueRow {
  id: string
  key: string
  value: string
  masked?: boolean
  multiline?: boolean
}

export interface EditSecretModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onSuccess?: () => void
}

export function EditSecretModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onSuccess,
}: EditSecretModalProps) {
  const { data: secret, isLoading } = useSecretDetail(clusterId, namespace, name, true)
  const updateSecretMutation = useUpdateSecret(clusterId)

  const [secretType, setSecretType] = useState('Opaque')
  const [rows, setRows] = useState<KeyValueRow[]>([])
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [initialized, setInitialized] = useState(false)

  // Pre-fill state when secret data arrives
  useEffect(() => {
    if (secret && !initialized) {
      setSecretType(secret.type || 'Opaque')
      const entries = Object.entries(secret.data || {})
      if (entries.length > 0) {
        setRows(
          entries.map(([k, v], idx) => ({
            id: String(idx + 1),
            key: k,
            value: v,
            masked: false,
            multiline: v.includes('\n') || v.length > 80,
          }))
        )
      } else {
        setRows([{ id: '1', key: '', value: '', masked: false }])
      }
      setInitialized(true)
    }
  }, [secret, initialized])

  // Reset initialization when modal opens/closes or target changes
  useEffect(() => {
    if (!open) {
      setInitialized(false)
      setErrorMessage(null)
      setRows([])
    }
  }, [open, name, namespace, clusterId])

  const handleAddRow = () => {
    setRows((prev) => [
      ...prev,
      { id: String(Date.now()), key: '', value: '', masked: false, multiline: false },
    ])
  }

  const handleRemoveRow = (id: string) => {
    setRows((prev) => (prev.length > 1 ? prev.filter((r) => r.id !== id) : prev))
  }

  const handleRowChange = (id: string, field: 'key' | 'value', val: string) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r.id !== id) return r
        const updated = { ...r, [field]: val }
        if (field === 'value' && (val.includes('\n') || val.length > 80)) {
          updated.multiline = true
        }
        return updated
      })
    )
  }

  const handleToggleMask = (id: string) => {
    setRows((prev) =>
      prev.map((r) => (r.id === id ? { ...r, masked: !r.masked } : r))
    )
  }

  const handleToggleMultiline = (id: string) => {
    setRows((prev) =>
      prev.map((r) => (r.id === id ? { ...r, multiline: !r.multiline } : r))
    )
  }

  const handleQuickEncode = (id: string) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r.id !== id) return r
        return { ...r, value: encodeUriPassword(r.value) }
      })
    )
  }

  const handleQuickDecode = (id: string) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r.id !== id) return r
        return { ...r, value: decodeUriPassword(r.value) }
      })
    )
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    // Validation
    for (const r of rows) {
      if (!r.key.trim() && r.value.trim()) {
        setErrorMessage('All entries with values must specify a key name.')
        return
      }
    }

    const stringData: Record<string, string> = {}
    for (const r of rows) {
      if (r.key.trim()) {
        stringData[r.key.trim()] = autoEncodeSecretForSubmission(r.value)
      }
    }

    try {
      const res = await updateSecretMutation.mutateAsync({
        namespaceName: namespace,
        name,
        payload: {
          name,
          namespace,
          type: secretType,
          stringData,
        },
      })

      if (res.success) {
        onSuccess?.()
        onClose()
      } else {
        setErrorMessage(res.message || 'Failed to update secret.')
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'An error occurred while updating the secret.')
    }
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xl">
      <form onSubmit={handleSave} className="flex flex-col h-full">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-2 text-amber-400">
            <KeyRound className="h-5 w-5 shrink-0" />
            <DialogTitle className="text-zinc-100 font-mono">
              Edit Secret: {name}
            </DialogTitle>
            <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
              {namespace}
            </Badge>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {isLoading ? (
            <div className="p-12 text-center text-xs text-zinc-500 flex flex-col items-center justify-center gap-3">
              <Loader2 className="h-6 w-6 animate-spin text-amber-400" />
              <span>Loading secret data...</span>
            </div>
          ) : (
            <>
              {errorMessage && (
                <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800/80 text-rose-300 text-xs flex items-start gap-2.5">
                  <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                  <div className="flex-1 whitespace-pre-wrap font-sans">{errorMessage}</div>
                </div>
              )}

              {/* Secret Meta Info */}
              <div className="grid grid-cols-2 gap-3 p-3 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs">
                <div>
                  <span className="text-zinc-500 text-[10px] uppercase tracking-wider block">
                    Type
                  </span>
                  <span className="font-mono text-zinc-200">{secretType}</span>
                </div>
                <div>
                  <span className="text-zinc-500 text-[10px] uppercase tracking-wider block">
                    Namespace
                  </span>
                  <span className="font-mono text-zinc-200">{namespace}</span>
                </div>
              </div>

              {/* Key-Value Entries */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    Secret Data ({rows.length} {rows.length === 1 ? 'entry' : 'entries'})
                  </span>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={handleAddRow}
                    className="h-7 text-xs gap-1 border-dashed border-zinc-700 text-zinc-300 hover:text-white hover:border-zinc-500"
                  >
                    <Plus className="h-3 w-3" />
                    Add Key
                  </Button>
                </div>

                <div className="space-y-3 max-h-96 overflow-y-auto pr-1">
                  {rows.map((row) => {
                    const isConn = isConnectionString(row.value)
                    const needsEncoding = isConn && hasUnencodedUriPassword(row.value)

                    return (
                      <div
                        key={row.id}
                        className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 space-y-2"
                      >
                        <div className="flex items-center gap-2">
                          <Input
                            placeholder="KEY_NAME (e.g. dsn, password)"
                            value={row.key}
                            onChange={(e) => handleRowChange(row.id, 'key', e.target.value)}
                            className="font-mono text-xs w-1/3 bg-zinc-900/90 border-zinc-800 text-purple-300 placeholder:text-zinc-600"
                            required
                          />
                          <div className="flex items-center gap-1.5 flex-1 justify-end">
                            <button
                              type="button"
                              onClick={() => handleToggleMask(row.id)}
                              className="text-zinc-400 hover:text-zinc-200 p-1 text-[11px] flex items-center gap-1 rounded bg-zinc-900 border border-zinc-800 hover:bg-zinc-800 cursor-pointer"
                              title={row.masked ? 'Reveal value' : 'Mask value'}
                            >
                              {row.masked ? (
                                <Eye className="h-3.5 w-3.5" />
                              ) : (
                                <EyeOff className="h-3.5 w-3.5" />
                              )}
                              <span className="text-[10px]">
                                {row.masked ? 'Reveal' : 'Mask'}
                              </span>
                            </button>
                            <button
                              type="button"
                              onClick={() => handleToggleMultiline(row.id)}
                              className={`p-1 text-[11px] flex items-center gap-1 rounded border cursor-pointer ${
                                row.multiline
                                  ? 'bg-purple-950/40 text-purple-300 border-purple-800'
                                  : 'text-zinc-400 hover:text-zinc-200 bg-zinc-900 border-zinc-800 hover:bg-zinc-800'
                              }`}
                              title="Toggle multiline editor"
                            >
                              <AlignLeft className="h-3.5 w-3.5" />
                              <span className="text-[10px]">Multiline</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => handleRemoveRow(row.id)}
                              className="text-zinc-500 hover:text-rose-400 p-1 rounded hover:bg-rose-950/30 transition-colors cursor-pointer"
                              title="Remove entry"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          </div>
                        </div>

                        {/* Value Input */}
                        {row.multiline ? (
                          <textarea
                            rows={4}
                            placeholder="Value..."
                            value={row.value}
                            onChange={(e) => handleRowChange(row.id, 'value', e.target.value)}
                            className="w-full rounded-md bg-zinc-900/90 border border-zinc-800 p-2 text-xs font-mono text-zinc-100 placeholder:text-zinc-600 focus:outline-none focus:ring-1 focus:ring-purple-500"
                          />
                        ) : (
                          <Input
                            type={row.masked ? 'password' : 'text'}
                            placeholder="Value..."
                            value={row.value}
                            onChange={(e) => handleRowChange(row.id, 'value', e.target.value)}
                            className="font-mono text-xs bg-zinc-900/90 border-zinc-800 text-zinc-100 placeholder:text-zinc-600"
                          />
                        )}

                        {/* URL Encoding Helper */}
                        {isConn && (
                          <div className="p-2 rounded bg-zinc-900/60 border border-zinc-800/80 text-[11px] flex items-center justify-between gap-2 flex-wrap">
                            <div className="flex items-center gap-1.5 text-zinc-300">
                              <Link2 className="h-3.5 w-3.5 text-sky-400 shrink-0" />
                              <span>
                                {needsEncoding ? (
                                  <span className="text-amber-300">
                                    Special characters in DSN password detected.
                                  </span>
                                ) : (
                                  <span className="text-zinc-400">
                                    Valid connection string format.
                                  </span>
                                )}
                              </span>
                            </div>
                            <div className="flex items-center gap-1.5">
                              {needsEncoding && (
                                <button
                                  type="button"
                                  onClick={() => handleQuickEncode(row.id)}
                                  className="px-2 py-0.5 rounded text-[10px] font-medium bg-amber-950/60 text-amber-300 border border-amber-800/80 hover:bg-amber-900/80 transition-colors flex items-center gap-1 cursor-pointer"
                                  title="Safely URL-encode the password in this connection string"
                                >
                                  <Sparkles className="h-2.5 w-2.5" />
                                  Encode Password
                                </button>
                              )}
                              <button
                                type="button"
                                onClick={() => handleQuickDecode(row.id)}
                                className="px-2 py-0.5 rounded text-[10px] font-medium bg-zinc-800/70 text-zinc-300 border border-zinc-700 hover:bg-zinc-700 transition-colors cursor-pointer"
                                title="Decode percent-encoded password back to plaintext"
                              >
                                Decode Plaintext
                              </button>
                            </div>
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              </div>
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
            disabled={isLoading || updateSecretMutation.isPending}
            className="bg-amber-600 hover:bg-amber-500 text-white font-semibold gap-1.5"
          >
            {updateSecretMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <KeyRound className="h-4 w-4" />
            )}
            Save Secret Changes
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

export interface EditConfigMapModalProps {
  open: boolean
  onClose: () => void
  clusterId: string
  namespace: string
  name: string
  onSuccess?: () => void
}

export function EditConfigMapModal({
  open,
  onClose,
  clusterId,
  namespace,
  name,
  onSuccess,
}: EditConfigMapModalProps) {
  const { data: configMap, isLoading } = useConfigMapDetail(clusterId, namespace, name)
  const updateConfigMapMutation = useUpdateConfigMap(clusterId)

  const [rows, setRows] = useState<KeyValueRow[]>([])
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [initialized, setInitialized] = useState(false)

  // Pre-fill state when configMap data arrives
  useEffect(() => {
    if (configMap && !initialized) {
      const entries = Object.entries(configMap.data || {})
      if (entries.length > 0) {
        setRows(
          entries.map(([k, v], idx) => ({
            id: String(idx + 1),
            key: k,
            value: v,
            masked: false,
            multiline: v.includes('\n') || v.length > 80,
          }))
        )
      } else {
        setRows([{ id: '1', key: '', value: '', masked: false }])
      }
      setInitialized(true)
    }
  }, [configMap, initialized])

  useEffect(() => {
    if (!open) {
      setInitialized(false)
      setErrorMessage(null)
      setRows([])
    }
  }, [open, name, namespace, clusterId])

  const handleAddRow = () => {
    setRows((prev) => [
      ...prev,
      { id: String(Date.now()), key: '', value: '', masked: false, multiline: false },
    ])
  }

  const handleRemoveRow = (id: string) => {
    setRows((prev) => (prev.length > 1 ? prev.filter((r) => r.id !== id) : prev))
  }

  const handleRowChange = (id: string, field: 'key' | 'value', val: string) => {
    setRows((prev) =>
      prev.map((r) => {
        if (r.id !== id) return r
        const updated = { ...r, [field]: val }
        if (field === 'value' && (val.includes('\n') || val.length > 80)) {
          updated.multiline = true
        }
        return updated
      })
    )
  }

  const handleToggleMultiline = (id: string) => {
    setRows((prev) =>
      prev.map((r) => (r.id === id ? { ...r, multiline: !r.multiline } : r))
    )
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)

    for (const r of rows) {
      if (!r.key.trim() && r.value.trim()) {
        setErrorMessage('All entries with values must specify a key name.')
        return
      }
    }

    const data: Record<string, string> = {}
    for (const r of rows) {
      if (r.key.trim()) {
        data[r.key.trim()] = r.value
      }
    }

    try {
      const res = await updateConfigMapMutation.mutateAsync({
        namespaceName: namespace,
        name,
        payload: {
          name,
          namespace,
          data,
        },
      })

      if (res.success) {
        onSuccess?.()
        onClose()
      } else {
        setErrorMessage(res.message || 'Failed to update config map.')
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'An error occurred while updating the config map.')
    }
  }

  if (!open) return null

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xl">
      <form onSubmit={handleSave} className="flex flex-col h-full">
        <DialogHeader onClose={onClose}>
          <div className="flex items-center gap-2 text-sky-400">
            <FileText className="h-5 w-5 shrink-0" />
            <DialogTitle className="text-zinc-100 font-mono">
              Edit ConfigMap: {name}
            </DialogTitle>
            <Badge variant="outline" className="text-[10px] font-mono py-0 ml-2">
              {namespace}
            </Badge>
          </div>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {isLoading ? (
            <div className="p-12 text-center text-xs text-zinc-500 flex flex-col items-center justify-center gap-3">
              <Loader2 className="h-6 w-6 animate-spin text-sky-400" />
              <span>Loading config map data...</span>
            </div>
          ) : (
            <>
              {errorMessage && (
                <div className="p-3 rounded-lg bg-rose-950/40 border border-rose-800/80 text-rose-300 text-xs flex items-start gap-2.5">
                  <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                  <div className="flex-1 whitespace-pre-wrap font-sans">{errorMessage}</div>
                </div>
              )}

              {/* Meta Info */}
              <div className="p-3 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs flex items-center justify-between">
                <div>
                  <span className="text-zinc-500 text-[10px] uppercase tracking-wider block">
                    Namespace
                  </span>
                  <span className="font-mono text-zinc-200">{namespace}</span>
                </div>
              </div>

              {/* Key-Value Entries */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-semibold text-zinc-400 uppercase tracking-wider">
                    Data Entries ({rows.length})
                  </span>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={handleAddRow}
                    className="h-7 text-xs gap-1 border-dashed border-zinc-700 text-zinc-300 hover:text-white hover:border-zinc-500"
                  >
                    <Plus className="h-3 w-3" />
                    Add Key
                  </Button>
                </div>

                <div className="space-y-3 max-h-96 overflow-y-auto pr-1">
                  {rows.map((row) => (
                    <div
                      key={row.id}
                      className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 space-y-2"
                    >
                      <div className="flex items-center gap-2">
                        <Input
                          placeholder="KEY_NAME (e.g. config.json, APP_ENV)"
                          value={row.key}
                          onChange={(e) => handleRowChange(row.id, 'key', e.target.value)}
                          className="font-mono text-xs w-1/3 bg-zinc-900/90 border-zinc-800 text-sky-300 placeholder:text-zinc-600"
                          required
                        />
                        <div className="flex items-center gap-1.5 flex-1 justify-end">
                          <button
                            type="button"
                            onClick={() => handleToggleMultiline(row.id)}
                            className={`p-1 text-[11px] flex items-center gap-1 rounded border cursor-pointer ${
                              row.multiline
                                ? 'bg-sky-950/40 text-sky-300 border-sky-800'
                                : 'text-zinc-400 hover:text-zinc-200 bg-zinc-900 border-zinc-800 hover:bg-zinc-800'
                            }`}
                            title="Toggle multiline editor"
                          >
                            <AlignLeft className="h-3.5 w-3.5" />
                            <span className="text-[10px]">Multiline</span>
                          </button>
                          <button
                            type="button"
                            onClick={() => handleRemoveRow(row.id)}
                            className="text-zinc-500 hover:text-rose-400 p-1 rounded hover:bg-rose-950/30 transition-colors cursor-pointer"
                            title="Remove entry"
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        </div>
                      </div>

                      {/* Value Input */}
                      {row.multiline ? (
                        <textarea
                          rows={5}
                          placeholder="Value..."
                          value={row.value}
                          onChange={(e) => handleRowChange(row.id, 'value', e.target.value)}
                          className="w-full rounded-md bg-zinc-900/90 border border-zinc-800 p-2 text-xs font-mono text-zinc-100 placeholder:text-zinc-600 focus:outline-none focus:ring-1 focus:ring-sky-500"
                        />
                      ) : (
                        <Input
                          type="text"
                          placeholder="Value..."
                          value={row.value}
                          onChange={(e) => handleRowChange(row.id, 'value', e.target.value)}
                          className="font-mono text-xs bg-zinc-900/90 border-zinc-800 text-zinc-100 placeholder:text-zinc-600"
                        />
                      )}
                    </div>
                  ))}
                </div>
              </div>
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
            disabled={isLoading || updateConfigMapMutation.isPending}
            className="bg-sky-600 hover:bg-sky-500 text-white font-semibold gap-1.5"
          >
            {updateConfigMapMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <FileText className="h-4 w-4" />
            )}
            Save ConfigMap Changes
          </Button>
        </DialogFooter>
      </form>
    </Dialog>
  )
}

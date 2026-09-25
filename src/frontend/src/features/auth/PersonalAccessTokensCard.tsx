import React, { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Key,
  Plus,
  Trash2,
  Copy,
  Check,
  Shield,
  Bot,
  AlertTriangle,
  Loader2,
} from 'lucide-react'
import {
  fetchApiTokens,
  createApiToken,
  revokeApiToken,
  type ApiTokenSummaryDto,
  type ApiTokenCreatedDto,
} from '../../api/tokens'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import {
  Dialog,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '../../components/ui/dialog'

export function PersonalAccessTokensCard() {
  const queryClient = useQueryClient()
  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [createdToken, setCreatedToken] = useState<ApiTokenCreatedDto | null>(null)

  // Form fields
  const [tokenName, setTokenName] = useState('')
  const [tokenRole, setTokenRole] = useState<'Admin' | 'Operator' | 'Viewer'>('Admin')
  const [expiresInDays, setExpiresInDays] = useState<number | ''>(90)
  const [copiedToken, setCopiedToken] = useState(false)
  const [copiedMcpConfig, setCopiedMcpConfig] = useState(false)

  const { data: tokens, isLoading, error } = useQuery<ApiTokenSummaryDto[]>({
    queryKey: ['api-tokens'],
    queryFn: fetchApiTokens,
  })

  const createMutation = useMutation({
    mutationFn: createApiToken,
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['api-tokens'] })
      setCreatedToken(data)
      setCreateModalOpen(false)
      setTokenName('')
      setTokenRole('Admin')
      setExpiresInDays(90)
    },
  })

  const revokeMutation = useMutation({
    mutationFn: revokeApiToken,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-tokens'] })
    },
  })

  const handleCreate = (e: React.FormEvent) => {
    e.preventDefault()
    if (!tokenName.trim()) return

    createMutation.mutate({
      name: tokenName.trim(),
      role: tokenRole,
      expiresInDays: expiresInDays === '' ? null : Number(expiresInDays),
    })
  }

  const handleCopyToken = () => {
    if (!createdToken) return
    navigator.clipboard.writeText(createdToken.token)
    setCopiedToken(true)
    setTimeout(() => setCopiedToken(false), 2000)
  }

  const currentHost = typeof window !== 'undefined' ? window.location.host : 'controlplane.homelab.local'
  const currentProto = typeof window !== 'undefined' ? window.location.protocol : 'https:'
  const mcpServerUrl = `${currentProto}//${currentHost}/mcp`

  const mcpSnippet = createdToken
    ? JSON.stringify(
        {
          mcpServers: {
            controlplane: {
              serverUrl: mcpServerUrl,
              headers: {
                'X-ControlPlane-Key': createdToken.token,
              },
            },
          },
        },
        null,
        2
      )
    : ''

  const handleCopyMcpConfig = () => {
    if (!mcpSnippet) return
    navigator.clipboard.writeText(mcpSnippet)
    setCopiedMcpConfig(true)
    setTimeout(() => setCopiedMcpConfig(false), 2000)
  }

  return (
    <div className="p-6 bg-zinc-900/60 border border-zinc-800 rounded-xl space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-zinc-800/80 pb-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Key className="w-5 h-5 text-sky-400" />
            <h3 className="text-base font-semibold text-zinc-100">Personal Access Tokens & Machine Keys</h3>
          </div>
          <p className="text-xs text-zinc-400 leading-relaxed max-w-xl">
            Generate scoped API tokens for Model Context Protocol (MCP) AI assistants (Cursor, Claude Desktop, Antigravity) and external automation without sharing root cluster credentials.
          </p>
        </div>
        <Button
          onClick={() => setCreateModalOpen(true)}
          className="bg-sky-600 hover:bg-sky-500 text-white gap-2 font-medium shrink-0 text-xs"
        >
          <Plus className="w-3.5 h-3.5" />
          Generate New Token
        </Button>
      </div>

      {/* Tokens List */}
      {isLoading ? (
        <div className="flex items-center justify-center py-8 text-zinc-500 text-xs gap-2">
          <Loader2 className="w-4 h-4 animate-spin text-sky-400" />
          <span>Loading tokens...</span>
        </div>
      ) : error ? (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg text-rose-400 text-xs">
          Failed to load Personal Access Tokens.
        </div>
      ) : tokens && tokens.length > 0 ? (
        <div className="divide-y divide-zinc-800/60 border border-zinc-800/80 rounded-lg bg-zinc-950/40 overflow-hidden">
          {tokens.map((t) => (
            <div
              key={t.id}
              className="p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 hover:bg-zinc-900/40 transition-colors"
            >
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm text-zinc-200">{t.name}</span>
                  <Badge
                    variant={
                      t.role === 'Admin' ? 'default' : t.role === 'Operator' ? 'purple' : 'warning'
                    }
                    className="text-[10px] px-1.5 py-0.5"
                  >
                    {t.role}
                  </Badge>
                  {t.isRevoked && (
                    <Badge variant="warning" className="text-[10px] bg-rose-500/10 text-rose-400 border-rose-500/20">
                      Revoked
                    </Badge>
                  )}
                  {t.isExpired && !t.isRevoked && (
                    <Badge variant="warning" className="text-[10px] bg-amber-500/10 text-amber-400 border-amber-500/20">
                      Expired
                    </Badge>
                  )}
                </div>
                <div className="flex flex-wrap items-center gap-3 text-[11px] text-zinc-500 font-mono">
                  <span>Prefix: {t.tokenPrefix}</span>
                  <span>•</span>
                  <span>Created: {new Date(t.createdAt).toLocaleDateString()}</span>
                  {t.expiresAt && (
                    <>
                      <span>•</span>
                      <span className={t.isExpired ? 'text-amber-500 font-medium' : ''}>
                        Expires: {new Date(t.expiresAt).toLocaleDateString()}
                      </span>
                    </>
                  )}
                  {t.lastUsedAt && (
                    <>
                      <span>•</span>
                      <span className="text-zinc-400">
                        Last used: {new Date(t.lastUsedAt).toLocaleDateString()}
                      </span>
                    </>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    if (confirm(`Revoke token "${t.name}"? Any MCP client or agent using it will lose access immediately.`)) {
                      revokeMutation.mutate(t.id)
                    }
                  }}
                  disabled={revokeMutation.isPending}
                  className="text-xs text-rose-400 hover:text-rose-300 hover:bg-rose-500/10 border-zinc-800"
                >
                  <Trash2 className="w-3.5 h-3.5 mr-1" />
                  Revoke
                </Button>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-8 px-4 border border-dashed border-zinc-800 rounded-xl bg-zinc-950/20 space-y-2">
          <Bot className="w-8 h-8 text-zinc-600 mx-auto" />
          <p className="text-xs font-medium text-zinc-300">No Personal Access Tokens Generated</p>
          <p className="text-[11px] text-zinc-500 max-w-sm mx-auto">
            Create an API token to connect your local AI development environment (Cursor, Claude Desktop, Antigravity) to this ControlPlane cluster.
          </p>
        </div>
      )}

      {/* Create Token Modal */}
      <Dialog open={createModalOpen} onClose={() => setCreateModalOpen(false)} maxWidth="md">
        <DialogHeader onClose={() => setCreateModalOpen(false)}>
          <DialogTitle className="flex items-center gap-2 text-base text-zinc-100">
            <Key className="w-4 h-4 text-sky-400" />
            Generate Personal Access Token
          </DialogTitle>
        </DialogHeader>

        <form onSubmit={handleCreate}>
          <DialogBody className="space-y-4 py-2">
            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Token Name <span className="text-rose-400">*</span>
              </label>
              <Input
                required
                placeholder="e.g. Claude Desktop MCP or Cursor Assistant"
                value={tokenName}
                onChange={(e) => setTokenName(e.target.value)}
                className="bg-zinc-950 border-zinc-800 text-xs"
              />
              <p className="text-[11px] text-zinc-500 mt-1">
                A descriptive name to remember where this token is being used.
              </p>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Role / Permission Level <span className="text-rose-400">*</span>
              </label>
              <div className="grid grid-cols-3 gap-2">
                {(['Admin', 'Operator', 'Viewer'] as const).map((role) => (
                  <button
                    key={role}
                    type="button"
                    onClick={() => setTokenRole(role)}
                    className={`p-2.5 rounded-lg border text-left text-xs transition-colors ${
                      tokenRole === role
                        ? 'border-sky-500 bg-sky-500/10 text-sky-300'
                        : 'border-zinc-800 bg-zinc-950/60 text-zinc-400 hover:border-zinc-700'
                    }`}
                  >
                    <div className="font-medium text-zinc-200">{role}</div>
                    <div className="text-[10px] text-zinc-500 mt-0.5">
                      {role === 'Admin' ? 'Full Access' : role === 'Operator' ? 'Deploy & Restart' : 'Read-Only'}
                    </div>
                  </button>
                ))}
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-zinc-300 mb-1.5">
                Expiration
              </label>
              <select
                value={expiresInDays === null ? 'never' : expiresInDays}
                onChange={(e) => {
                  const val = e.target.value
                  setExpiresInDays(val === 'never' ? '' : Number(val))
                }}
                className="w-full px-3 py-2 text-xs bg-zinc-950 border border-zinc-800 rounded-lg text-zinc-200 focus:outline-none focus:border-sky-500"
              >
                <option value={30}>30 days</option>
                <option value={90}>90 days (Recommended)</option>
                <option value={365}>1 year</option>
                <option value="never">No expiration</option>
              </select>
            </div>
          </DialogBody>

          <DialogFooter className="pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setCreateModalOpen(false)}
              disabled={createMutation.isPending}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={createMutation.isPending || !tokenName.trim()}
              className="bg-sky-600 hover:bg-sky-500 text-white gap-2 font-medium text-xs"
            >
              {createMutation.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
              Generate Token
            </Button>
          </DialogFooter>
        </form>
      </Dialog>

      {/* New Token Revealed Modal */}
      <Dialog
        open={Boolean(createdToken)}
        onClose={() => setCreatedToken(null)}
        maxWidth="lg"
      >
        <DialogHeader onClose={() => setCreatedToken(null)}>
          <DialogTitle className="flex items-center gap-2 text-base text-zinc-100">
            <Shield className="w-4 h-4 text-emerald-400" />
            Token Generated Successfully
          </DialogTitle>
        </DialogHeader>

        <DialogBody className="space-y-4 py-2">
          <div className="p-3 bg-amber-500/10 border border-amber-500/20 rounded-lg flex items-start gap-2.5 text-xs text-amber-300">
            <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
            <span>
              Make sure to copy your Personal Access Token now. <strong>You will not be able to see it again!</strong>
            </span>
          </div>

          <div>
            <label className="block text-xs font-medium text-zinc-300 mb-1.5">
              Token ({createdToken?.name})
            </label>
            <div className="flex items-center gap-2">
              <input
                readOnly
                value={createdToken?.token || ''}
                className="w-full px-3 py-2 text-xs font-mono bg-zinc-950 border border-zinc-800 rounded-lg text-emerald-400 select-all focus:outline-none"
              />
              <Button
                type="button"
                variant="secondary"
                onClick={handleCopyToken}
                className="shrink-0 gap-1.5 text-xs"
              >
                {copiedToken ? (
                  <>
                    <Check className="w-3.5 h-3.5 text-emerald-400" />
                    <span className="text-emerald-400">Copied</span>
                  </>
                ) : (
                  <>
                    <Copy className="w-3.5 h-3.5" />
                    <span>Copy</span>
                  </>
                )}
              </Button>
            </div>
          </div>

          {/* MCP Config Snippet */}
          <div className="space-y-1.5 pt-1">
            <div className="flex items-center justify-between">
              <label className="text-xs font-medium text-zinc-300 flex items-center gap-1.5">
                <Bot className="w-3.5 h-3.5 text-sky-400" />
                MCP Client Configuration (Cursor / Claude / Antigravity)
              </label>
              <button
                type="button"
                onClick={handleCopyMcpConfig}
                className="text-[11px] text-sky-400 hover:text-sky-300 flex items-center gap-1 transition-colors"
              >
                {copiedMcpConfig ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                <span>{copiedMcpConfig ? 'Copied' : 'Copy JSON'}</span>
              </button>
            </div>
            <pre className="p-3 bg-zinc-950 border border-zinc-800 rounded-lg text-[11px] font-mono text-zinc-300 overflow-x-auto">
              {mcpSnippet}
            </pre>
          </div>
        </DialogBody>

        <DialogFooter>
          <Button
            type="button"
            onClick={() => setCreatedToken(null)}
            className="bg-emerald-600 hover:bg-emerald-500 text-white font-medium text-xs"
          >
            Done & I Have Saved My Token
          </Button>
        </DialogFooter>
      </Dialog>
    </div>
  )
}

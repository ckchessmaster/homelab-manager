import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchProxmoxConfig,
  saveProxmoxConfig,
  probeProxmox,
  type SaveProxmoxConfigPayload,
  type ProxmoxProbePayload,
} from '../../api/adapters'

export const ADAPTERS_QUERY_KEY = ['adapters']
export const PROXMOX_CONFIG_QUERY_KEY = ['adapters', 'proxmox', 'config']

export function useProxmoxConfig() {
  return useQuery({
    queryKey: PROXMOX_CONFIG_QUERY_KEY,
    queryFn: fetchProxmoxConfig,
    staleTime: 1000 * 60 * 5, // 5 minutes
  })
}

export function useSaveProxmoxConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveProxmoxConfigPayload) => saveProxmoxConfig(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROXMOX_CONFIG_QUERY_KEY })
    },
  })
}

export function useProbeProxmox() {
  return useMutation({
    mutationFn: (payload: ProxmoxProbePayload) => probeProxmox(payload),
  })
}

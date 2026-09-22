import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchProxmoxConfig,
  saveProxmoxConfig,
  probeProxmox,
  fetchProxmoxInstances,
  fetchProxmoxInstance,
  fetchProxmoxVitals,
  saveProxmoxInstance,
  deleteProxmoxInstance,
  testProxmoxInstanceConnection,
  type SaveProxmoxConfigPayload,
  type ProxmoxProbePayload,
  type SaveProxmoxInstancePayload,
} from '../../api/adapters'

export const ADAPTERS_QUERY_KEY = ['adapters']
export const PROXMOX_CONFIG_QUERY_KEY = ['adapters', 'proxmox', 'config']
export const PROXMOX_INSTANCES_QUERY_KEY = ['adapters', 'proxmox', 'instances']

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
      queryClient.invalidateQueries({ queryKey: PROXMOX_INSTANCES_QUERY_KEY })
    },
  })
}

export function useProbeProxmox() {
  return useMutation({
    mutationFn: (payload: ProxmoxProbePayload) => probeProxmox(payload),
  })
}

export function useProxmoxInstances() {
  return useQuery({
    queryKey: PROXMOX_INSTANCES_QUERY_KEY,
    queryFn: fetchProxmoxInstances,
    staleTime: 1000 * 60 * 2,
  })
}

export function useProxmoxInstance(id: string) {
  return useQuery({
    queryKey: [...PROXMOX_INSTANCES_QUERY_KEY, id],
    queryFn: () => fetchProxmoxInstance(id),
    enabled: Boolean(id),
  })
}

export function useSaveProxmoxInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveProxmoxInstancePayload) => saveProxmoxInstance(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROXMOX_INSTANCES_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: PROXMOX_CONFIG_QUERY_KEY })
    },
  })
}

export function useDeleteProxmoxInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteProxmoxInstance(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROXMOX_INSTANCES_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: PROXMOX_CONFIG_QUERY_KEY })
    },
  })
}

export function useTestProxmoxInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => testProxmoxInstanceConnection(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: [...PROXMOX_INSTANCES_QUERY_KEY, id, 'vitals'] })
    },
  })
}

export function useProxmoxVitals(id?: string | null, options?: { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: [...PROXMOX_INSTANCES_QUERY_KEY, id, 'vitals'],
    queryFn: () => (id ? fetchProxmoxVitals(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
    refetchInterval: options?.refetchInterval ?? 10000,
  })
}

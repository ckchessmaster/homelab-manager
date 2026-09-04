import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createHost,
  deleteHost,
  fetchHostById,
  fetchHosts,
  probeProxmox,
  updateHost,
  type CreateHostPayload,
  type HostFilterParams,
  type ProxmoxProbePayload,
  type UpdateHostPayload,
} from '../../api/hosts'

export const HOSTS_QUERY_KEY = ['hosts']

export function useHosts(filters?: HostFilterParams) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, filters],
    queryFn: () => fetchHosts(filters),
    refetchInterval: 10000,
  })
}

export function useHost(id?: string) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, id],
    queryFn: () => (id ? fetchHostById(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
  })
}

export function useCreateHost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateHostPayload) => createHost(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    },
  })
}

export function useUpdateHost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateHostPayload }) =>
      updateHost(id, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    },
  })
}

export function useDeleteHost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteHost(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    },
  })
}

export function useProxmoxProbe() {
  return useMutation({
    mutationFn: (payload: ProxmoxProbePayload) => probeProxmox(payload),
  })
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createHost,
  deleteHost,
  fetchHostById,
  fetchHosts,
  fetchHostCorrelation,
  fetchHostRebootImpact,
  fetchHostVitals,
  probeProxmox,
  rebootHost,
  syncHostCorrelations,
  updateHost,
  fetchHostHardware,
  triggerHardwareScan,
  fetchHardwareThresholds,
  updateHardwareThresholds,
  type CreateHostPayload,
  type HostFilterParams,
  type ProxmoxProbePayload,
  type UpdateHostPayload,
  type HardwareThresholds,
} from '../../api/hosts'
import { JOBS_QUERY_KEY } from '../orchestration/useJobs'

export const HOSTS_QUERY_KEY = ['hosts']

export function useHosts(filters?: HostFilterParams) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, filters],
    queryFn: () => fetchHosts(filters),
    refetchInterval: 10000,
  })
}

export function useHost(id?: string, options?: { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, id],
    queryFn: () => (id ? fetchHostById(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
    refetchInterval: options?.refetchInterval,
  })
}

export function useHostVitals(id?: string | null, options?: { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, id, 'vitals'],
    queryFn: () => (id ? fetchHostVitals(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
    refetchInterval: options?.refetchInterval ?? 5000,
  })
}

export function useHostRebootImpact(id?: string) {
  return useQuery({
    queryKey: ['host-reboot-impact', id],
    queryFn: () => (id ? fetchHostRebootImpact(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
  })
}

export function useHostCorrelation(id?: string) {
  return useQuery({
    queryKey: ['host-correlation', id],
    queryFn: () => (id ? fetchHostCorrelation(id) : Promise.reject('No ID provided')),
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

export function useRebootHost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (param: string | { hostId: string; pipelineId?: string; force?: boolean }) => {
      if (typeof param === 'string') {
        return rebootHost(param)
      }
      return rebootHost(param.hostId, param.pipelineId, param.force ?? false)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: JOBS_QUERY_KEY })
    },
  })
}

export function useProxmoxProbe() {
  return useMutation({
    mutationFn: (payload: ProxmoxProbePayload) => probeProxmox(payload),
  })
}

export function useSyncHostCorrelations() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => syncHostCorrelations(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useHostHardware(id?: string | null, options?: { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: [...HOSTS_QUERY_KEY, id, 'hardware'],
    queryFn: () => (id ? fetchHostHardware(id) : Promise.reject('No ID provided')),
    enabled: Boolean(id),
    refetchInterval: options?.refetchInterval ?? 30000,
  })
}

export function useTriggerHardwareScan() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (hostId: string) => triggerHardwareScan(hostId),
    onSuccess: (_, hostId) => {
      queryClient.invalidateQueries({ queryKey: [...HOSTS_QUERY_KEY, hostId, 'hardware'] })
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    },
  })
}

export function useHardwareThresholds() {
  return useQuery({
    queryKey: ['system-settings', 'hardware-thresholds'],
    queryFn: () => fetchHardwareThresholds(),
  })
}

export function useUpdateHardwareThresholds() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (thresholds: HardwareThresholds) => updateHardwareThresholds(thresholds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['system-settings', 'hardware-thresholds'] })
      queryClient.invalidateQueries({ queryKey: HOSTS_QUERY_KEY })
    },
  })
}


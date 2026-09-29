import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchOPNsenseInstances,
  fetchOPNsenseInstance,
  saveOPNsenseInstance,
  deleteOPNsenseInstance,
  testOPNsenseConnection,
  testSavedOPNsenseConnection,
  fetchOPNsenseTelemetry,
  fetchOPNsenseServices,
  restartOPNsenseService,
  fetchOPNsenseDhcpLeases,
  fetchOPNsenseFirmware,
  checkOPNsenseFirmware,
  fetchOPNsenseVitals,
  fetchOPNsenseHAProxy,
  fetchOPNsenseAcme,
  fetchOPNsenseSecurityAlerts,
  fetchOPNsenseFirewallStats,
  fetchOPNsenseArp,
  type SaveOPNsenseInstancePayload,
} from '../../../api/opnsense'

export function useOPNsenseInstances() {
  return useQuery({
    queryKey: ['opnsense', 'instances'],
    queryFn: fetchOPNsenseInstances,
    refetchInterval: 15000,
  })
}

export function useOPNsenseInstance(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'instances', id],
    queryFn: () => fetchOPNsenseInstance(id!),
    enabled: !!id,
  })
}

export function useSaveOPNsenseInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveOPNsenseInstancePayload) => saveOPNsenseInstance(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['opnsense', 'instances'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useDeleteOPNsenseInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteOPNsenseInstance(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['opnsense', 'instances'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useTestOPNsenseConnection() {
  return useMutation({
    mutationFn: (payload: SaveOPNsenseInstancePayload) => testOPNsenseConnection(payload),
  })
}

export function useTestSavedOPNsenseConnection() {
  return useMutation({
    mutationFn: (id: string) => testSavedOPNsenseConnection(id),
  })
}

export function useOPNsenseTelemetry(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'telemetry', id],
    queryFn: () => fetchOPNsenseTelemetry(id!),
    enabled: !!id,
    refetchInterval: 10000,
  })
}

export function useOPNsenseServices(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'services', id],
    queryFn: () => fetchOPNsenseServices(id!),
    enabled: !!id,
    refetchInterval: 10000,
  })
}

export function useRestartOPNsenseService(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (serviceName: string) => restartOPNsenseService(instanceId, serviceName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['opnsense', 'telemetry', instanceId] })
      queryClient.invalidateQueries({ queryKey: ['opnsense', 'services', instanceId] })
    },
  })
}

export function useOPNsenseDhcpLeases(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'dhcp', 'leases', id],
    queryFn: () => fetchOPNsenseDhcpLeases(id!),
    enabled: !!id,
    refetchInterval: 15000,
  })
}

export function useOPNsenseFirmware(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'firmware', id],
    queryFn: () => fetchOPNsenseFirmware(id!),
    enabled: !!id,
    refetchInterval: 30000,
  })
}

export function useCheckOPNsenseFirmware(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => checkOPNsenseFirmware(instanceId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['opnsense', 'firmware', instanceId] })
    },
  })
}

export function useOPNsenseVitals(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'vitals', id],
    queryFn: () => fetchOPNsenseVitals(id!),
    enabled: !!id,
    refetchInterval: 10000,
  })
}

export function useOPNsenseHAProxy(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'haproxy', id],
    queryFn: () => fetchOPNsenseHAProxy(id!),
    enabled: !!id,
    refetchInterval: 15000,
  })
}

export function useOPNsenseAcme(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'acme', id],
    queryFn: () => fetchOPNsenseAcme(id!),
    enabled: !!id,
    refetchInterval: 30000,
  })
}

export function useOPNsenseSecurityAlerts(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'security-alerts', id],
    queryFn: () => fetchOPNsenseSecurityAlerts(id!),
    enabled: !!id,
    refetchInterval: 15000,
  })
}

export function useOPNsenseFirewallStats(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'firewall-stats', id],
    queryFn: () => fetchOPNsenseFirewallStats(id!),
    enabled: !!id,
    refetchInterval: 15000,
  })
}

export function useOPNsenseArp(id: string | null) {
  return useQuery({
    queryKey: ['opnsense', 'arp', id],
    queryFn: () => fetchOPNsenseArp(id!),
    enabled: !!id,
    refetchInterval: 20000,
  })
}

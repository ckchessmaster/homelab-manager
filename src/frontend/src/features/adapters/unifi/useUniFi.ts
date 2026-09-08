import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchUniFiInstances,
  fetchUniFiInstance,
  saveUniFiInstance,
  deleteUniFiInstance,
  testUniFiInstanceConnection,
  testUniFiPreflight,
  fetchUniFiDevices,
  restartUniFiDevice,
  upgradeUniFiDevice,
  powerCycleUniFiPort,
  fetchUniFiClients,
  type SaveUniFiInstancePayload,
} from '../../../api/unifi'

export function useUniFiInstances() {
  return useQuery({
    queryKey: ['unifi', 'instances'],
    queryFn: fetchUniFiInstances,
    refetchInterval: 15000,
  })
}

export function useUniFiInstance(id: string | null) {
  return useQuery({
    queryKey: ['unifi', 'instances', id],
    queryFn: () => fetchUniFiInstance(id!),
    enabled: !!id,
  })
}

export function useSaveUniFiInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveUniFiInstancePayload) => saveUniFiInstance(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['unifi', 'instances'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useDeleteUniFiInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteUniFiInstance(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['unifi', 'instances'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useTestUniFiConnection() {
  return useMutation({
    mutationFn: (id: string) => testUniFiInstanceConnection(id),
  })
}

export function useTestUniFiPreflight() {
  return useMutation({
    mutationFn: (payload: SaveUniFiInstancePayload) => testUniFiPreflight(payload),
  })
}

export function useUniFiDevices(instanceId: string | null) {
  return useQuery({
    queryKey: ['unifi', instanceId, 'devices'],
    queryFn: () => fetchUniFiDevices(instanceId!),
    enabled: !!instanceId,
    refetchInterval: 20000,
  })
}

export function useRestartUniFiDevice(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ deviceMac, reason }: { deviceMac: string; reason?: string }) =>
      restartUniFiDevice(instanceId, deviceMac, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['unifi', instanceId, 'devices'] })
    },
  })
}

export function useUpgradeUniFiDevice(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (deviceMac: string) => upgradeUniFiDevice(instanceId, deviceMac),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['unifi', instanceId, 'devices'] })
    },
  })
}

export function usePowerCycleUniFiPort(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ deviceMac, portIdx, delaySeconds }: { deviceMac: string; portIdx: number; delaySeconds?: number }) =>
      powerCycleUniFiPort(instanceId, deviceMac, portIdx, delaySeconds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['unifi', instanceId, 'devices'] })
    },
  })
}

export function useUniFiClients(instanceId: string | null) {
  return useQuery({
    queryKey: ['unifi', instanceId, 'clients'],
    queryFn: () => fetchUniFiClients(instanceId!),
    enabled: !!instanceId,
    refetchInterval: 30000,
  })
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchIdracInstances,
  fetchIdracInstance,
  saveIdracInstance,
  deleteIdracInstance,
  testIdracConnection,
  testSavedIdracConnection,
  fetchIdracVitals,
  sendIdracPowerAction,
  sendIdracPowerActionByIp,
  installIpmiTool,
  sendBmcFanControl,
  sendBmcFanControlByIp,
  sendBmcChassisIdentify,
  sendBmcChassisIdentifyByIp,
  sendBmcBootOverride,
  sendBmcBootOverrideByIp,
  type SaveIdracInstancePayload,
  type BmcFanControlPayload,
  type BmcChassisIdentifyPayload,
  type BmcBootOverridePayload,
} from '../../../api/idrac'

export function useIdracInstances() {
  return useQuery({
    queryKey: ['idrac', 'instances'],
    queryFn: fetchIdracInstances,
    refetchInterval: 15000,
  })
}

export function useIdracInstance(id: string | null) {
  return useQuery({
    queryKey: ['idrac', 'instances', id],
    queryFn: () => fetchIdracInstance(id!),
    enabled: !!id,
  })
}

export function useSaveIdracInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveIdracInstancePayload) => saveIdracInstance(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['idrac', 'instances'] })
    },
  })
}

export function useDeleteIdracInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteIdracInstance(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['idrac', 'instances'] })
    },
  })
}

export function useTestIdracConnection() {
  return useMutation({
    mutationFn: (payload: SaveIdracInstancePayload) => testIdracConnection(payload),
  })
}

export function useTestSavedIdracConnection() {
  return useMutation({
    mutationFn: (id: string) => testSavedIdracConnection(id),
  })
}

export function useIdracVitals(id: string | null) {
  return useQuery({
    queryKey: ['idrac', 'vitals', id],
    queryFn: () => fetchIdracVitals(id!),
    enabled: !!id,
    refetchInterval: 10000,
  })
}

export function useIdracPowerAction(instanceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (resetType: string) => sendIdracPowerAction(instanceId, resetType),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['idrac', 'vitals', instanceId] })
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

export function useIdracPowerActionByIp() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      idracIp,
      resetType,
      username,
      password,
      hostId,
    }: {
      idracIp?: string
      resetType: string
      username?: string
      password?: string
      hostId?: string
    }) => sendIdracPowerActionByIp(idracIp, resetType, username, password, hostId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
      queryClient.invalidateQueries({ queryKey: ['adapters', 'idrac'] })
    },
  })
}

export function useInstallIpmiTool() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (hostId: string) => installIpmiTool(hostId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

// --- Fan Control ---

export function useIdracFanControl(instanceId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: BmcFanControlPayload) => {
      if (!instanceId) throw new Error('Instance ID is required.')
      return sendBmcFanControl(instanceId, payload)
    },
    onSuccess: () => {
      if (instanceId) {
        queryClient.invalidateQueries({ queryKey: ['idrac', 'vitals', instanceId] })
      }
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

export function useIdracFanControlByIp() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: {
      idracIp?: string
      hostId?: string
      mode: string
      percentage?: number
      username?: string
      password?: string
    }) => sendBmcFanControlByIp(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
      queryClient.invalidateQueries({ queryKey: ['adapters', 'idrac'] })
      queryClient.invalidateQueries({ queryKey: ['idrac', 'vitals'] })
    },
  })
}

// --- Chassis Identify (Locator LED / UID) ---

export function useIdracIdentify(instanceId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: BmcChassisIdentifyPayload) => {
      if (!instanceId) throw new Error('Instance ID is required.')
      return sendBmcChassisIdentify(instanceId, payload)
    },
    onSuccess: () => {
      if (instanceId) {
        queryClient.invalidateQueries({ queryKey: ['idrac', 'vitals', instanceId] })
      }
    },
  })
}

export function useIdracIdentifyByIp() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: {
      idracIp?: string
      hostId?: string
      state: string
      durationSeconds?: number
      username?: string
      password?: string
    }) => sendBmcChassisIdentifyByIp(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
      queryClient.invalidateQueries({ queryKey: ['adapters', 'idrac'] })
    },
  })
}

// --- Boot Device Override ---

export function useIdracBootOverride(instanceId?: string) {
  return useMutation({
    mutationFn: (payload: BmcBootOverridePayload) => {
      if (!instanceId) throw new Error('Instance ID is required.')
      return sendBmcBootOverride(instanceId, payload)
    },
  })
}

export function useIdracBootOverrideByIp() {
  return useMutation({
    mutationFn: (payload: {
      idracIp?: string
      hostId?: string
      target: string
      username?: string
      password?: string
    }) => sendBmcBootOverrideByIp(payload),
  })
}

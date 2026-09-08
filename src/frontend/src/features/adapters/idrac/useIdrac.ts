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
  type SaveIdracInstancePayload,
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
    }: {
      idracIp: string
      resetType: string
      username?: string
      password?: string
    }) => sendIdracPowerActionByIp(idracIp, resetType, username, password),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

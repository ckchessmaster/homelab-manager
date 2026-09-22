import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  homeAssistantApi,
  type SaveHomeAssistantInstancePayload,
  type CreateBackupRequest,
} from '../../../api/homeAssistant'

export function useHomeAssistantInstances() {
  return useQuery({
    queryKey: ['homeassistant', 'instances'],
    queryFn: homeAssistantApi.getInstances,
    refetchInterval: 15000,
  })
}

export function useHomeAssistantInstance(id: string | null) {
  return useQuery({
    queryKey: ['homeassistant', 'instances', id],
    queryFn: () => homeAssistantApi.getInstance(id!),
    enabled: !!id,
  })
}

export function useHomeAssistantOverview(id: string | null) {
  return useQuery({
    queryKey: ['homeassistant', 'overview', id],
    queryFn: () => homeAssistantApi.getOverview(id!),
    enabled: !!id,
    refetchInterval: 10000,
  })
}

export function useHomeAssistantBackups(id: string | null) {
  return useQuery({
    queryKey: ['homeassistant', 'backups', id],
    queryFn: () => homeAssistantApi.getBackups(id!),
    enabled: !!id,
    refetchInterval: 30000,
  })
}

export function useSaveHomeAssistantInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveHomeAssistantInstancePayload) => homeAssistantApi.saveInstance(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'instances'] })
    },
  })
}

export function useDeleteHomeAssistantInstance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.deleteInstance(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'instances'] })
    },
  })
}

export function useTestHomeAssistantConnection() {
  return useMutation({
    mutationFn: (payload: SaveHomeAssistantInstancePayload) => homeAssistantApi.testConnection(payload),
  })
}

export function useTestSavedHomeAssistantConnection() {
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.testInstanceConnection(id),
  })
}

export function useCheckHomeAssistantConfig() {
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.checkConfig(id),
  })
}

export function useRestartHomeAssistantCore() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.restartCore(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'overview', id] })
    },
  })
}

export function useRebootHomeAssistantHost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.rebootHost(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'overview', id] })
    },
  })
}

export function useUpdateHomeAssistantOs() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => homeAssistantApi.updateOs(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'overview', id] })
    },
  })
}

export function useCreateHomeAssistantBackup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id: string; request?: CreateBackupRequest }) =>
      homeAssistantApi.createBackup(id, request),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'backups', id] })
      queryClient.invalidateQueries({ queryKey: ['homeassistant', 'overview', id] })
    },
  })
}

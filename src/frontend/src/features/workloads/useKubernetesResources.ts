import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  listSecrets,
  getSecret,
  createSecret,
  updateSecret,
  deleteSecret,
  listConfigMaps,
  getConfigMap,
  createConfigMap,
  updateConfigMap,
  deleteConfigMap,
  type CreateSecretPayload,
  type CreateConfigMapPayload,
} from '../../api/kubernetesResources'

// --- Secrets Hooks ---

export function useSecrets(clusterId?: string, namespaceName?: string) {
  return useQuery({
    queryKey: ['kubernetes', 'secrets', clusterId, namespaceName],
    queryFn: () => {
      if (!clusterId) return []
      return listSecrets(clusterId, namespaceName)
    },
    enabled: Boolean(clusterId),
    staleTime: 10_000,
  })
}

export function useSecretDetail(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  reveal: boolean = false
) {
  return useQuery({
    queryKey: ['kubernetes', 'secret', clusterId, namespaceName, name, reveal],
    queryFn: () => {
      if (!clusterId || !namespaceName || !name) return null
      return getSecret(clusterId, namespaceName, name, reveal)
    },
    enabled: Boolean(clusterId && namespaceName && name),
  })
}

export function useCreateSecret(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateSecretPayload) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return createSecret(clusterId, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'secrets'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useUpdateSecret(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      namespaceName,
      name,
      payload,
    }: {
      namespaceName: string
      name: string
      payload: CreateSecretPayload
    }) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return updateSecret(clusterId, namespaceName, name, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'secrets'] })
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'secret'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useDeleteSecret(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ namespaceName, name }: { namespaceName: string; name: string }) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return deleteSecret(clusterId, namespaceName, name)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'secrets'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

// --- ConfigMaps Hooks ---

export function useConfigMaps(clusterId?: string, namespaceName?: string) {
  return useQuery({
    queryKey: ['kubernetes', 'configmaps', clusterId, namespaceName],
    queryFn: () => {
      if (!clusterId) return []
      return listConfigMaps(clusterId, namespaceName)
    },
    enabled: Boolean(clusterId),
    staleTime: 10_000,
  })
}

export function useConfigMapDetail(
  clusterId?: string,
  namespaceName?: string,
  name?: string
) {
  return useQuery({
    queryKey: ['kubernetes', 'configmap', clusterId, namespaceName, name],
    queryFn: () => {
      if (!clusterId || !namespaceName || !name) return null
      return getConfigMap(clusterId, namespaceName, name)
    },
    enabled: Boolean(clusterId && namespaceName && name),
  })
}

export function useCreateConfigMap(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateConfigMapPayload) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return createConfigMap(clusterId, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'configmaps'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useUpdateConfigMap(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      namespaceName,
      name,
      payload,
    }: {
      namespaceName: string
      name: string
      payload: CreateConfigMapPayload
    }) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return updateConfigMap(clusterId, namespaceName, name, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'configmaps'] })
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'configmap'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useDeleteConfigMap(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ namespaceName, name }: { namespaceName: string; name: string }) => {
      if (!clusterId) throw new Error('Cluster ID is required.')
      return deleteConfigMap(clusterId, namespaceName, name)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'configmaps'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

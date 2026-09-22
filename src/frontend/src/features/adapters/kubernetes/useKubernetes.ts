import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchKubernetesClusters,
  fetchKubernetesCluster,
  fetchKubernetesClusterVitals,
  saveKubernetesCluster,
  deleteKubernetesCluster,
  testKubernetesClusterConnection,
  testKubernetesClusterPreflight,
  fetchClusterNodes,
  cordonClusterNode,
  uncordonClusterNode,
  drainClusterNode,
  fetchClusterNamespaces,
  fetchClusterDeployments,
  restartClusterDeployment,
  scaleClusterDeployment,
  fetchClusterPods,
  type SaveKubernetesClusterPayload,
} from '../../../api/kubernetes'

export function useKubernetesClusters() {
  return useQuery({
    queryKey: ['kubernetes', 'clusters'],
    queryFn: fetchKubernetesClusters,
    refetchInterval: 15000,
  })
}

export function useKubernetesCluster(id: string | null) {
  return useQuery({
    queryKey: ['kubernetes', 'clusters', id],
    queryFn: () => fetchKubernetesCluster(id!),
    enabled: !!id,
  })
}

export function useKubernetesClusterVitals(id: string | null, options?: { refetchInterval?: number | false }) {
  return useQuery({
    queryKey: ['kubernetes', 'clusters', id, 'vitals'],
    queryFn: () => fetchKubernetesClusterVitals(id!),
    enabled: !!id,
    refetchInterval: options?.refetchInterval ?? 10000,
  })
}

export function useSaveKubernetesCluster() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: SaveKubernetesClusterPayload) => saveKubernetesCluster(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'clusters'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useDeleteKubernetesCluster() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteKubernetesCluster(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'clusters'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

export function useTestKubernetesClusterConnection() {
  return useMutation({
    mutationFn: (id: string) => testKubernetesClusterConnection(id),
  })
}

export function useTestKubernetesClusterPreflight() {
  return useMutation({
    mutationFn: (payload: SaveKubernetesClusterPayload) => testKubernetesClusterPreflight(payload),
  })
}

export function useClusterNodes(clusterId: string | null) {
  return useQuery({
    queryKey: ['kubernetes', 'nodes', clusterId],
    queryFn: () => fetchClusterNodes(clusterId!),
    enabled: !!clusterId,
    refetchInterval: 10000,
  })
}

export function useCordonClusterNode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ clusterId, nodeName }: { clusterId: string; nodeName: string }) =>
      cordonClusterNode(clusterId, nodeName),
    onSuccess: (_, { clusterId }) => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'nodes', clusterId] })
    },
  })
}

export function useUncordonClusterNode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ clusterId, nodeName }: { clusterId: string; nodeName: string }) =>
      uncordonClusterNode(clusterId, nodeName),
    onSuccess: (_, { clusterId }) => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'nodes', clusterId] })
    },
  })
}

export function useDrainClusterNode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ clusterId, nodeName, timeoutSeconds }: { clusterId: string; nodeName: string; timeoutSeconds?: number }) =>
      drainClusterNode(clusterId, nodeName, timeoutSeconds),
    onSuccess: (_, { clusterId }) => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'nodes', clusterId] })
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'pods', clusterId] })
    },
  })
}

export function useClusterNamespaces(clusterId: string | null) {
  return useQuery({
    queryKey: ['kubernetes', 'namespaces', clusterId],
    queryFn: () => fetchClusterNamespaces(clusterId!),
    enabled: !!clusterId,
  })
}

export function useClusterDeployments(clusterId: string | null, namespace?: string) {
  return useQuery({
    queryKey: ['kubernetes', 'deployments', clusterId, namespace],
    queryFn: () => fetchClusterDeployments(clusterId!, namespace),
    enabled: !!clusterId,
    refetchInterval: 10000,
  })
}

export function useRestartClusterDeployment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ clusterId, namespace, name }: { clusterId: string; namespace: string; name: string }) =>
      restartClusterDeployment(clusterId, namespace, name),
    onSuccess: (_, { clusterId }) => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'deployments', clusterId] })
    },
  })
}

export function useScaleClusterDeployment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ clusterId, namespace, name, replicas }: { clusterId: string; namespace: string; name: string; replicas: number }) =>
      scaleClusterDeployment(clusterId, namespace, name, replicas),
    onSuccess: (_, { clusterId }) => {
      queryClient.invalidateQueries({ queryKey: ['kubernetes', 'deployments', clusterId] })
    },
  })
}

export function useClusterPods(clusterId: string | null, namespace?: string, nodeName?: string) {
  return useQuery({
    queryKey: ['kubernetes', 'pods', clusterId, namespace, nodeName],
    queryFn: () => fetchClusterPods(clusterId!, namespace, nodeName),
    enabled: !!clusterId,
    refetchInterval: 10000,
  })
}

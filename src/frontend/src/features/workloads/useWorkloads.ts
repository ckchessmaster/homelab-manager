import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getWorkloads,
  getWorkloadPods,
  restartWorkload,
  recreateWorkloadPods,
  scaleWorkload,
  triggerCronJob,
  getAppBundle,
  applyManifestYaml,
  deleteAppBundle,
  getIngresses,
  getCertificates,
  getStorageOverview,
  getClusterVitals,
  deleteNamespace,
  createNamespace,
  type WorkloadAggregationResult,
  type PodSummary,
  type AppBundle,
  type IngressSummary,
  type CertificateSummary,
  type StorageOverview,
  type ClusterVitals,
  type DeleteOptions,
} from '../../api/workloads'

export function useWorkloads(clusterId?: string, namespaceName?: string) {
  return useQuery<WorkloadAggregationResult>({
    queryKey: ['workloads', clusterId || 'all', namespaceName || 'all'],
    queryFn: () => getWorkloads(clusterId, namespaceName),
    refetchInterval: 10000,
  })
}

export function useWorkloadPods(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  enabled: boolean = true
) {
  return useQuery<PodSummary[]>({
    queryKey: ['workloadPods', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getWorkloadPods(clusterId, namespaceName, name)
        : Promise.resolve([]),
    enabled: Boolean(enabled && clusterId && namespaceName && name),
    refetchInterval: 5000,
  })
}

export function useRestartWorkload() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      kind,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      kind?: string
    }) => restartWorkload(clusterId, namespaceName, name, kind),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}

export function useRecreateWorkloadPods() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      kind,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      kind?: string
    }) => recreateWorkloadPods(clusterId, namespaceName, name, kind),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}

export function useScaleWorkload() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      replicas,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      replicas: number
    }) => scaleWorkload(clusterId, namespaceName, name, replicas),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}

export function useTriggerCronJob() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
    }: {
      clusterId: string
      namespaceName: string
      name: string
    }) => triggerCronJob(clusterId, namespaceName, name),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
    },
  })
}

export function useAppBundle(clusterId?: string, namespaceName?: string, name?: string) {
  return useQuery<AppBundle>({
    queryKey: ['appBundle', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getAppBundle(clusterId, namespaceName, name)
        : Promise.reject(new Error('Missing cluster, namespace, or app name')),
    enabled: Boolean(clusterId && namespaceName && name),
  })
}

export function useApplyManifestYaml() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      yamlContent,
      dryRun,
    }: {
      clusterId: string
      yamlContent: string
      dryRun?: boolean
    }) => applyManifestYaml(clusterId, yamlContent, dryRun),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['ingresses'] })
      queryClient.invalidateQueries({ queryKey: ['storage'] })
    },
  })
}

export function useDeleteAppBundle() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      options,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      options: DeleteOptions
    }) => deleteAppBundle(clusterId, namespaceName, name, options),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['ingresses'] })
      queryClient.invalidateQueries({ queryKey: ['storage'] })
    },
  })
}

export function useIngresses(clusterId?: string, namespaceName?: string) {
  return useQuery<IngressSummary[]>({
    queryKey: ['ingresses', clusterId, namespaceName || 'all'],
    queryFn: () => (clusterId ? getIngresses(clusterId, namespaceName) : Promise.resolve([])),
    enabled: Boolean(clusterId),
    refetchInterval: 15000,
  })
}

export function useCertificates(clusterId?: string, namespaceName?: string) {
  return useQuery<CertificateSummary[]>({
    queryKey: ['certificates', clusterId, namespaceName || 'all'],
    queryFn: () => (clusterId ? getCertificates(clusterId, namespaceName) : Promise.resolve([])),
    enabled: Boolean(clusterId),
    refetchInterval: 20000,
  })
}

export function useStorageOverview(clusterId?: string, namespaceName?: string) {
  return useQuery<StorageOverview>({
    queryKey: ['storage', clusterId, namespaceName || 'all'],
    queryFn: () => (clusterId ? getStorageOverview(clusterId, namespaceName) : Promise.resolve({
      totalPvcs: 0,
      boundPvcs: 0,
      totalCapacityBytes: 0,
      pvcs: [],
      storageClasses: [],
      longhornDetected: false,
    })),
    enabled: Boolean(clusterId),
    refetchInterval: 15000,
  })
}

export function useClusterVitals(clusterId?: string) {
  return useQuery<ClusterVitals>({
    queryKey: ['vitals', clusterId],
    queryFn: () => (clusterId ? getClusterVitals(clusterId) : Promise.resolve({
      metricsServerAvailable: false,
      totalCpuUsageMillis: 0,
      totalCpuAllocatableMillis: 0,
      totalMemoryUsageBytes: 0,
      totalMemoryAllocatableBytes: 0,
      nodes: [],
      topPods: [],
    })),
    enabled: Boolean(clusterId),
    refetchInterval: 10000,
  })
}

export function useDeleteNamespace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      confirmedName,
    }: {
      clusterId: string
      namespaceName: string
      confirmedName: string
    }) => deleteNamespace(clusterId, namespaceName, confirmedName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['k8s-namespaces'] })
      queryClient.invalidateQueries({ queryKey: ['ingresses'] })
      queryClient.invalidateQueries({ queryKey: ['certificates'] })
      queryClient.invalidateQueries({ queryKey: ['storage'] })
      queryClient.invalidateQueries({ queryKey: ['vitals'] })
    },
  })
}

export function useCreateNamespace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      name,
      labels,
      annotations,
    }: {
      clusterId: string
      name: string
      labels?: Record<string, string>
      annotations?: Record<string, string>
    }) => createNamespace(clusterId, name, labels, annotations),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['k8s-namespaces'] })
    },
  })
}



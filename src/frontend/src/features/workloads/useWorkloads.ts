import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getWorkloads,
  getWorkloadPods,
  restartWorkload,
  recreateWorkloadPods,
  scaleWorkload,
  triggerCronJob,
  getAppBundle,
  getResourceYaml,
  type ResourceYamlResult,
  applyManifestYaml,
  deleteAppBundle,
  getIngresses,
  getServices,
  getService,
  updateService,
  type ServiceSummary,
  type K8sServiceDetail,
  type UpdateServicePayload,
  getIngress,
  updateIngress,
  getCertificates,
  getStorageOverview,
  getClusterVitals,
  deleteNamespace,
  createNamespace,
  getPodLogs,
  getWorkloadRevisions,
  rollbackWorkloadRevision,
  getClusterEvents,
  checkImageUpdates,
  getCachedImageUpdates,
  getImageTags,
  updateWorkloadImage,
  type UpdateWorkloadImagePayload,
  type WorkloadAggregationResult,
  type PodSummary,
  type AppBundle,
  type IngressSummary,
  type K8sIngressDetail,
  type UpdateIngressPayload,
  type CertificateSummary,
  type StorageOverview,
  type ClusterVitals,
  type DeleteOptions,
  type RolloutRevision,
  type ClusterEvent,
} from '../../api/workloads'

export function useWorkloads(clusterId?: string, namespaceName?: string, includeAll = false) {
  return useQuery<WorkloadAggregationResult>({
    queryKey: ['workloads', clusterId || 'all', namespaceName || 'all', includeAll ? 'all' : 'main'],
    queryFn: () => getWorkloads(clusterId, namespaceName, includeAll),
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

export function useResourceYaml(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  kind?: string
) {
  return useQuery<ResourceYamlResult>({
    queryKey: ['resourceYaml', clusterId, namespaceName, name, kind],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getResourceYaml(clusterId, namespaceName, name, kind)
        : Promise.reject(new Error('Missing cluster, namespace, or resource name')),
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

export function useServices(clusterId?: string, namespaceName?: string) {
  return useQuery<ServiceSummary[]>({
    queryKey: ['services', clusterId, namespaceName || 'all'],
    queryFn: () => (clusterId ? getServices(clusterId, namespaceName) : Promise.resolve([])),
    enabled: Boolean(clusterId),
    refetchInterval: 15000,
  })
}

export function useServiceDetail(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  enabled: boolean = true
) {
  return useQuery<K8sServiceDetail>({
    queryKey: ['serviceDetail', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getService(clusterId, namespaceName, name)
        : Promise.reject(new Error('Missing parameters')),
    enabled: Boolean(enabled && clusterId && namespaceName && name),
  })
}

export function useUpdateService(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      namespaceName,
      name,
      payload,
    }: {
      namespaceName: string
      name: string
      payload: UpdateServicePayload
    }) => {
      if (!clusterId) throw new Error('Cluster ID required')
      return updateService(clusterId, namespaceName, name, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['services'] })
      queryClient.invalidateQueries({ queryKey: ['serviceDetail'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
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

export function useIngressDetail(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  enabled: boolean = true
) {
  return useQuery<K8sIngressDetail>({
    queryKey: ['ingressDetail', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getIngress(clusterId, namespaceName, name)
        : Promise.reject(new Error('Missing parameters')),
    enabled: Boolean(enabled && clusterId && namespaceName && name),
  })
}

export function useUpdateIngress(clusterId?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      namespaceName,
      name,
      payload,
    }: {
      namespaceName: string
      name: string
      payload: UpdateIngressPayload
    }) => {
      if (!clusterId) throw new Error('Cluster ID required')
      return updateIngress(clusterId, namespaceName, name, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ingresses'] })
      queryClient.invalidateQueries({ queryKey: ['ingressDetail'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
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

export function usePodLogs(
  clusterId?: string,
  namespaceName?: string,
  podName?: string,
  container?: string,
  tailLines: number = 100,
  enabled: boolean = true
) {
  return useQuery<{ podName: string; container?: string; logs: string }>({
    queryKey: ['podLogs', clusterId, namespaceName, podName, container, tailLines],
    queryFn: () =>
      clusterId && namespaceName && podName
        ? getPodLogs(clusterId, namespaceName, podName, container, tailLines)
        : Promise.resolve({ podName: podName || '', logs: '' }),
    enabled: Boolean(enabled && clusterId && namespaceName && podName),
    refetchInterval: 4000,
  })
}

export function useWorkloadRevisions(
  clusterId?: string,
  namespaceName?: string,
  name?: string,
  enabled: boolean = true
) {
  return useQuery<RolloutRevision[]>({
    queryKey: ['workloadRevisions', clusterId, namespaceName, name],
    queryFn: () =>
      clusterId && namespaceName && name
        ? getWorkloadRevisions(clusterId, namespaceName, name)
        : Promise.resolve([]),
    enabled: Boolean(enabled && clusterId && namespaceName && name),
  })
}

export function useRollbackWorkloadRevision() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      revision,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      revision: number
    }) => rollbackWorkloadRevision(clusterId, namespaceName, name, revision),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
      queryClient.invalidateQueries({ queryKey: ['workloadRevisions'] })
    },
  })
}

export function useClusterEvents(
  clusterId?: string,
  namespaceName?: string,
  type?: string,
  enabled: boolean = true
) {
  return useQuery<ClusterEvent[]>({
    queryKey: ['clusterEvents', clusterId || 'all', namespaceName || 'all', type || 'all'],
    queryFn: () => getClusterEvents(clusterId, namespaceName, type),
    enabled,
    refetchInterval: 5000,
  })
}

export function useCachedImageUpdates() {
  return useQuery<Record<string, import('../../api/workloads').ImageUpdateInfo>>({
    queryKey: ['imageUpdates'],
    queryFn: () => getCachedImageUpdates(),
    refetchInterval: 30000,
  })
}

export function useCheckImageUpdates() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ images, force }: { images?: string[]; force?: boolean } = {}) =>
      checkImageUpdates(images, force),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['imageUpdates'] })
    },
  })
}

export function useImageTags(imageRef?: string, enabled = true) {
  return useQuery<string[]>({
    queryKey: ['image-tags', imageRef],
    queryFn: () => getImageTags(imageRef!),
    enabled: Boolean(enabled && imageRef && imageRef.trim()),
    staleTime: 1000 * 60 * 15,
  })
}

export function useUpdateWorkloadImage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      clusterId,
      namespaceName,
      name,
      payload,
    }: {
      clusterId: string
      namespaceName: string
      name: string
      payload: UpdateWorkloadImagePayload
    }) => updateWorkloadImage(clusterId, namespaceName, name, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
      queryClient.invalidateQueries({ queryKey: ['workloadPods'] })
      queryClient.invalidateQueries({ queryKey: ['workloadRevisions'] })
      queryClient.invalidateQueries({ queryKey: ['imageUpdates'] })
    },
  })
}





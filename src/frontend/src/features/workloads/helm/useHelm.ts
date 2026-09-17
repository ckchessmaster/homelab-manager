import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getHelmCatalog,
  getHelmReleases,
  getHelmReleaseDetail,
  getHelmReleaseHistory,
  installOrUpgradeHelmRelease,
  rollbackHelmRelease,
  uninstallHelmRelease,
  type InstallHelmReleasePayload,
  type HelmReleaseSummary,
  type HelmReleaseDetail,
  type HelmReleaseRevision,
  type HelmCatalogItem,
} from '../../../api/helm'

export function useHelmCatalog() {
  return useQuery<HelmCatalogItem[]>({
    queryKey: ['helm-catalog'],
    queryFn: getHelmCatalog,
    staleTime: 1000 * 60 * 30, // 30 minutes
  })
}

export function useHelmReleases(clusterId: string, namespaceName?: string) {
  return useQuery<HelmReleaseSummary[]>({
    queryKey: ['helm-releases', clusterId, namespaceName || 'all'],
    queryFn: () => getHelmReleases(clusterId, namespaceName),
    enabled: Boolean(clusterId),
    refetchInterval: 15000,
  })
}

export function useHelmReleaseDetail(clusterId: string, namespaceName: string, name: string) {
  return useQuery<HelmReleaseDetail>({
    queryKey: ['helm-release-detail', clusterId, namespaceName, name],
    queryFn: () => getHelmReleaseDetail(clusterId, namespaceName, name),
    enabled: Boolean(clusterId && namespaceName && name),
  })
}

export function useHelmReleaseHistory(clusterId: string, namespaceName: string, name: string) {
  return useQuery<HelmReleaseRevision[]>({
    queryKey: ['helm-release-history', clusterId, namespaceName, name],
    queryFn: () => getHelmReleaseHistory(clusterId, namespaceName, name),
    enabled: Boolean(clusterId && namespaceName && name),
  })
}

export function useInstallHelmRelease(clusterId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (payload: InstallHelmReleasePayload) =>
      installOrUpgradeHelmRelease(clusterId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['helm-releases', clusterId] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useRollbackHelmRelease(clusterId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      namespaceName,
      name,
      revision,
    }: {
      namespaceName: string
      name: string
      revision: number
    }) => rollbackHelmRelease(clusterId, namespaceName, name, revision),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['helm-releases', clusterId] })
      queryClient.invalidateQueries({ queryKey: ['helm-release-detail'] })
      queryClient.invalidateQueries({ queryKey: ['helm-release-history'] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

export function useUninstallHelmRelease(clusterId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ namespaceName, name }: { namespaceName: string; name: string }) =>
      uninstallHelmRelease(clusterId, namespaceName, name),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['helm-releases', clusterId] })
      queryClient.invalidateQueries({ queryKey: ['workloads'] })
    },
  })
}

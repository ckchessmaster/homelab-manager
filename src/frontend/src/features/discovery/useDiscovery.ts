import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { scanDiscovery, importCandidate } from '../../api/discovery'
import type { ImportCandidatePayload, DiscoveryScanResult, ImportCandidateResponse } from '../../api/discovery'

export function useDiscoveryScan(options?: {
  includeProxmox?: boolean
  includeKubernetes?: boolean
  enabled?: boolean
}) {
  return useQuery<DiscoveryScanResult, Error>({
    queryKey: ['discovery', 'scan', options?.includeProxmox ?? true, options?.includeKubernetes ?? true],
    queryFn: () => scanDiscovery(options),
    enabled: options?.enabled ?? true,
    staleTime: 30000,
  })
}

export function useImportCandidate() {
  const queryClient = useQueryClient()

  return useMutation<ImportCandidateResponse, Error, ImportCandidatePayload>({
    mutationFn: (payload) => importCandidate(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
      queryClient.invalidateQueries({ queryKey: ['discovery'] })
    },
  })
}

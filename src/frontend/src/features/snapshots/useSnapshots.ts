import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  fetchSnapshots,
  pruneSnapshots,
  deleteSnapshot,
  type HostSnapshotItem,
  type SnapshotPruneRequest,
  type SnapshotPruneResult,
} from '../../api/snapshots'

export const SNAPSHOTS_QUERY_KEY = ['snapshots']

export function useSnapshots(hostId?: string) {
  return useQuery<HostSnapshotItem[]>({
    queryKey: [...SNAPSHOTS_QUERY_KEY, hostId ?? 'all'],
    queryFn: () => fetchSnapshots(hostId),
    refetchInterval: 30000,
  })
}

export function usePruneSnapshots() {
  const queryClient = useQueryClient()

  return useMutation<SnapshotPruneResult, Error, SnapshotPruneRequest>({
    mutationFn: (req) => pruneSnapshots(req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SNAPSHOTS_QUERY_KEY })
    },
  })
}

export function useDeleteSnapshot() {
  const queryClient = useQueryClient()

  return useMutation<{ message: string }, Error, { hostId: string; snapshotName: string }>({
    mutationFn: ({ hostId, snapshotName }) => deleteSnapshot(hostId, snapshotName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SNAPSHOTS_QUERY_KEY })
    },
  })
}

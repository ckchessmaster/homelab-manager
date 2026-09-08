import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  adoptNode,
  adoptNodesBatch,
  type AdoptNodePayload,
  type NodeAdoptionResponse,
  type BatchAdoptNodesPayload,
  type BatchAdoptNodesResponse,
} from '../../api/hosts'

export function useAdoptNode() {
  const queryClient = useQueryClient()

  return useMutation<NodeAdoptionResponse, Error, AdoptNodePayload>({
    mutationFn: (payload: AdoptNodePayload) => adoptNode(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

export function useBatchAdoptNodes() {
  const queryClient = useQueryClient()

  return useMutation<BatchAdoptNodesResponse, Error, BatchAdoptNodesPayload>({
    mutationFn: (payload: BatchAdoptNodesPayload) => adoptNodesBatch(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}


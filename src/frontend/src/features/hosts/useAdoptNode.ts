import { useMutation, useQueryClient } from '@tanstack/react-query'
import { adoptNode, type AdoptNodePayload, type NodeAdoptionResponse } from '../../api/hosts'

export function useAdoptNode() {
  const queryClient = useQueryClient()

  return useMutation<NodeAdoptionResponse, Error, AdoptNodePayload>({
    mutationFn: (payload: AdoptNodePayload) => adoptNode(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fetchJobs, fetchJob, createJob } from '../../api/jobs'

export const JOBS_QUERY_KEY = ['jobs']

export function useJobs(hostId?: string) {
  return useQuery({
    queryKey: [...JOBS_QUERY_KEY, hostId],
    queryFn: () => fetchJobs(hostId),
    refetchInterval: 3000,
  })
}

export function useJob(jobId: string | null) {
  return useQuery({
    queryKey: ['job', jobId],
    queryFn: () => (jobId ? fetchJob(jobId) : null),
    enabled: Boolean(jobId),
    refetchInterval: (query) => {
      const data = query.state.data
      if (!data) return 2000
      if (data.status === 'Completed' || data.status === 'Failed' || data.status === 'RolledBack') {
        return false
      }
      return 1500
    },
  })
}

export function useCreateJob() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (targetHostId: string) => createJob(targetHostId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] })
      queryClient.invalidateQueries({ queryKey: ['hosts'] })
    },
  })
}

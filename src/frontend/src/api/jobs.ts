import { apiClient } from './client'

export interface JobSummary {
  id: string
  targetHostId: string
  pipelineId?: string | null
  status: 'Pending' | 'Running' | 'Verifying' | 'Completed' | 'Failed' | 'RolledBack' | string
  activeStep?: string | null
  initiatedBy: string
  startedAt?: string | null
  completedAt?: string | null
  failureReason?: string | null
}

export interface CreateJobRequest {
  targetHostId: string
  pipelineId?: string
}

export async function fetchJobs(hostId?: string, limit = 50): Promise<JobSummary[]> {
  const query = new URLSearchParams()
  if (hostId) query.set('hostId', hostId)
  if (limit) query.set('limit', String(limit))
  const qs = query.toString() ? `?${query.toString()}` : ''
  return apiClient<JobSummary[]>(`/api/v1/jobs${qs}`)
}

export async function fetchJob(id: string): Promise<JobSummary> {
  return apiClient<JobSummary>(`/api/v1/jobs/${id}`)
}

export async function createJob(targetHostId: string, pipelineId?: string): Promise<JobSummary> {
  return apiClient<JobSummary>('/api/v1/jobs', {
    method: 'POST',
    body: JSON.stringify({ targetHostId, pipelineId }),
  })
}

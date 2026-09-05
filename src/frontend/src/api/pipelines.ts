import { apiClient } from './client'

export interface PipelineStepSummary {
  name: string
  description: string
}

export interface PipelineProfile {
  id: string
  name: string
  description: string
  icon: string
  compatibleTargetTypes: string[]
  steps: PipelineStepSummary[]
}

export async function fetchPipelines(): Promise<PipelineProfile[]> {
  return apiClient<PipelineProfile[]>('/api/v1/pipelines')
}

import axios from 'axios'

type ProblemDetails = { title?: string; errors?: Record<string, string[]> }

export function getApiError(error: unknown, fallback: string) {
  if (!axios.isAxiosError<ProblemDetails>(error)) return fallback
  const problem = error.response?.data
  const validationMessage = problem?.errors && Object.values(problem.errors).flat()[0]
  return validationMessage ?? problem?.title ?? fallback
}

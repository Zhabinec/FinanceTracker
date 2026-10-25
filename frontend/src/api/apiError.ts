import axios from 'axios'

type ProblemDetails = { title?: string; errors?: Record<string, string[]> }

export function getApiError(error: unknown, fallback: string) {
  if (!axios.isAxiosError<ProblemDetails>(error)) return fallback
  const problem = error.response?.data
  if (error.response?.status === 429)
    return problem?.title ?? 'Слишком много запросов. Подождите немного и повторите попытку.'
  if (!error.response) return 'Нет соединения с сервером. Проверьте подключение и повторите попытку.'
  if ([502, 503, 504].includes(error.response.status)) return 'Сервис временно недоступен. Повторите попытку немного позже.'
  const validationMessage = problem?.errors && Object.values(problem.errors).flat()[0]
  return validationMessage ?? problem?.title ?? fallback
}

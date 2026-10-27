import { useQuery } from '@tanstack/react-query'
import { http } from '../../api/http'

export function LocalMailHint() {
  const { data } = useQuery({
    queryKey: ['email-delivery-mode'],
    queryFn: async () => (await http.get<{ simulated: boolean; restricted?: boolean; previewUrl: string | null }>('/auth/email-delivery')).data,
    staleTime: Infinity,
  })
  if (data?.restricted) return <p className="notice" role="status">Отправка писем пока доступна только владельцу тестовой версии.</p>
  if (!data?.simulated) return null
  return <p className="form-hint">В этой сборке включена имитация почты. Письма для указанного email приходят в тестовый ящик Mailpit.
    {data.previewUrl && <> <a href={data.previewUrl} target="_blank" rel="noreferrer">Открыть тестовую почту</a></>}
  </p>
}

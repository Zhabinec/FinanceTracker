import { useQuery } from '@tanstack/react-query'
import { http } from '../../api/http'
import { isTelegram } from '../../platform/telegram'

export function TelegramEntry() {
  const options = useQuery({
    queryKey: ['telegram-options'],
    enabled: !isTelegram(),
    queryFn: async () => (await http.get<{ enabled: boolean; botUsername: string }>('/auth/telegram/options')).data,
  })
  if (isTelegram()) return null
  if (options.isPending) return <p role="status">Загружаем способы входа…</p>
  if (options.isError) return <p role="alert">Не удалось загрузить Telegram.
    <button className="ghost-button" onClick={() => void options.refetch()}>Повторить</button></p>
  const username = options.data.botUsername
  if (!options.data.enabled || !/^[A-Za-z0-9_]{5,32}$/.test(username)) return null
  return <a className="primary-button telegram-entry" href={`https://t.me/${username}`} target="_blank" rel="noreferrer">Открыть в Telegram</a>
}

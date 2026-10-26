import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getApiError } from '../../api/apiError'
import { formatDate } from '../../shared/formatting'
import { useAuth } from '../auth/useAuth'
import { useCurrencyDisplay } from '../currencies/currencyDisplayContext'
import type { MoneyAccount } from './accountApi'
import { cancelTransfer, createTransfer, getTransfers } from './transferApi'
import { transferSchema } from './transferValidation'

export function TransfersPanel({ accounts, pending }: { accounts: MoneyAccount[]; pending: boolean }) {
  const { user } = useAuth()
  const display = useCurrencyDisplay()
  const client = useQueryClient()
  const [sourceId, setSourceId] = useState<string | null>(null)
  const [targetId, setTargetId] = useState<string | null>(null)
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [cancelError, setCancelError] = useState('')
  const [page, setPage] = useState(1)
  const requestRef = useRef<{ signature: string; id: string } | null>(null)
  const submitting = useRef(false)
  const active = accounts.filter(account => !account.isArchived)
  const from = sourceId ?? active.find(a => a.id === user?.id)?.id ?? active[0]?.id ?? ''
  const source = active.find(a => a.id === from)
  const destinations = active.filter(a => a.id !== from && a.currency === source?.currency)
  const to = targetId ?? destinations[0]?.id ?? ''
  const target = destinations.find(a => a.id === to)
  const refresh = () => Promise.all(['accounts', 'transfers'].map(key => client.invalidateQueries({ queryKey: [key] })))
  const history = useQuery({ queryKey: ['transfers', page], queryFn: ({ signal }) => getTransfers(page, signal) })
  const create = useMutation({ mutationFn: createTransfer, onSuccess: refresh })
  const cancel = useMutation({ mutationFn: cancelTransfer,
    onSuccess: async () => { await refresh(); setNotice('Перевод отменён. Балансы обновлены.') }, onError: refresh })

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting.current) return
    setError(''); setNotice('')
    const parsed = transferSchema.safeParse({ sourceAccountId: from, targetAccountId: to, amount, description })
    if (!parsed.success) { setError(parsed.error.issues[0].message); return }
    if (!source || !target) { setError('Выберите два активных счёта в одной валюте.'); return }
    const input = { ...parsed.data, amount: parsed.data.amount.replace(',', '.'), description: parsed.data.description || null }
    const signature = JSON.stringify(input)
    submitting.current = true
    try {
      if (requestRef.current?.signature !== signature) requestRef.current = { signature, id: crypto.randomUUID() }
      const saved = await create.mutateAsync({ ...input, requestId: requestRef.current.id })
      setNotice(saved.cancelledAtUtc ? 'Этот перевод уже был отменён.' : 'Перевод добавлен. Балансы обновлены.')
      setAmount(''); setDescription(''); setPage(1); requestRef.current = null
    } catch (failure) { setError(getApiError(failure, 'Не удалось создать перевод.')); void refresh() }
    finally { submitting.current = false }
  }

  return <>
    <details className="panel-disclosure transfer-create"><summary>Перевод между счетами</summary>
      {pending ? <p role="status">Загружаем счета…</p> : active.length < 2 ?
        <p>Для перевода нужны два активных счёта. Добавьте второй через «Новый счёт».</p> :
        <form className="transfer-form" onSubmit={event => void submit(event)} noValidate>
          {error && <div className="notice error form-wide" role="alert">{error}</div>}
          <label>Откуда<select value={from} disabled={create.isPending} onChange={event => { setSourceId(event.target.value); setTargetId(null) }}>
            {!source && <option value={from}>Счёт недоступен</option>}
            {active.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select></label>
          <label>Куда<select value={to} disabled={create.isPending} onChange={event => setTargetId(event.target.value)}>
            {!target && <option value={to}>Выберите счёт</option>}
            {destinations.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select></label>
          <label>Сумма ({source?.currency ?? '—'})<input inputMode="decimal" value={amount} placeholder="0,00"
            disabled={create.isPending} onChange={event => setAmount(event.target.value)} /></label>
          <label>Комментарий<input maxLength={500} value={description} placeholder="Необязательно" disabled={create.isPending} onChange={event => setDescription(event.target.value)} /></label>
          <button className="primary-button form-wide" disabled={create.isPending || !source || !target}>{create.isPending ? 'Переводим…' : 'Перевести'}</button>
        </form>}
    </details>
    {notice && <div className="notice success transfer-notice" role="status">{notice}</div>}
    <section className="transactions-list-card transfer-history"><h2>Переводы</h2>
      {cancelError && <div className="notice error" role="alert">{cancelError}</div>}
      {history.isPending && <p role="status">Загружаем переводы…</p>}
      {history.isError && <div className="notice error" role="alert">{getApiError(history.error, 'Не удалось загрузить переводы.')} <button className="text-button" onClick={() => void history.refetch()}>Повторить</button></div>}
      {history.isSuccess && history.data.items.length === 0 && <p className="dashboard-note">Переводов пока нет.</p>}
      <ul className="transfer-list">{history.data?.items.map(transfer => <li key={transfer.id} className={`transfer-row${transfer.cancelledAtUtc ? ' cancelled' : ''}`}>
        <div><strong>{transfer.sourceAccountName} → {transfer.targetAccountName}</strong>
          <span>{transfer.description ? `${transfer.description} · ` : ''}{formatDate(transfer.occurredOn)}{transfer.cancelledAtUtc ? ' · Отменён' : ''}</span></div>
        <strong className="transfer-amount">{display.money(transfer.amount, transfer.currency)}</strong>
        {!transfer.cancelledAtUtc && <button className="text-button danger-action" disabled={cancel.isPending} onClick={async () => {
          if (!window.confirm('Отменить перевод? Его влияние на оба баланса будет убрано, запись останется в истории.')) return
          setCancelError('')
          try { await cancel.mutateAsync(transfer) } catch (failure) { setCancelError(getApiError(failure, 'Не удалось отменить перевод.')) }
        }}>Отменить перевод</button>}
      </li>)}</ul>
      {history.data && history.data.totalCount > history.data.pageSize && <div className="pagination">
        <button className="ghost-button" disabled={page === 1 || history.isFetching} onClick={() => setPage(value => value - 1)}>Назад</button>
        <span>{page} / {Math.ceil(history.data.totalCount / history.data.pageSize)}</span>
        <button className="ghost-button" disabled={page * history.data.pageSize >= history.data.totalCount || history.isFetching} onClick={() => setPage(value => value + 1)}>Далее</button>
      </div>}
    </section>
  </>
}

import { useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { financeHttp as http } from '../privateFinance/financeHttp'
import { getApiError } from '../../api/apiError'
import { getCategories } from '../categories/categoryApi'
import { useAuth } from '../auth/useAuth'
import { useCurrencyDisplay } from '../currencies/currencyDisplayContext'
import { todayInMoscow, transactionSchema } from '../transactions/validation'
import { DataSkeleton } from '../../shared/DataSkeleton'

type Budget = { id: string; categoryId: string; categoryName: string; categoryActive: boolean; month: string;
  limit: string; spent: string; remaining: string; overspent: string; version: string }

export function BudgetsPanel() {
  const { user } = useAuth()
  const display = useCurrencyDisplay()
  const client = useQueryClient()
  const [month, setMonth] = useState(todayInMoscow().slice(0, 7))
  const [categoryId, setCategoryId] = useState('')
  const [limit, setLimit] = useState('')
  const [editing, setEditing] = useState<Budget | null>(null)
  const [busy, setBusy] = useState(false)
  const submitting = useRef(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const budgets = useQuery({ queryKey: ['budgets', month], enabled: /^\d{4}-\d{2}$/.test(month),
    queryFn: async ({ signal }) => (await http.get<Budget[]>('/budgets', { params: { month: `${month}-01` }, signal })).data })
  const categories = useQuery({ queryKey: ['categories'], queryFn: getCategories })
  const available = categories.data?.filter(c => c.type === 'Expense' && !budgets.data?.some(b => b.categoryId === c.id)) ?? []
  const selected = categoryId || available[0]?.id || ''
  const money = (value: string) => display.money(value, user?.baseCurrency ?? 'RUB')
  const clear = () => { setEditing(null); setLimit(''); setCategoryId('') }
  const refresh = () => client.invalidateQueries({ queryKey: ['budgets'] })
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting.current) return
    setError(''); setNotice('')
    const amount = transactionSchema.shape.amount.safeParse(limit)
    if (!amount.success) { setError(amount.error.issues[0].message); return }
    if (!editing && !available.some(c => c.id === selected)) { setError('Выберите расходную категорию.'); return }
    submitting.current = true; setBusy(true)
    try {
      if (editing) await http.put(`/budgets/${editing.id}`, { limit: amount.data.replace(',', '.'), version: editing.version })
      else await http.post('/budgets', { month: `${month}-01`, categoryId: selected, limit: amount.data.replace(',', '.') })
      clear(); setNotice('Лимит сохранён.')
    } catch (failure) { setError(getApiError(failure, 'Не удалось сохранить лимит.')) }
    finally { await refresh(); submitting.current = false; setBusy(false) }
  }
  const remove = async (budget: Budget) => {
    if (submitting.current || !window.confirm(`Убрать лимит «${budget.categoryName}»? Расходы останутся в истории.`)) return
    submitting.current = true; setBusy(true); setError(''); setNotice('')
    try { await http.delete(`/budgets/${budget.id}`, { params: { version: budget.version } }); clear(); setNotice('Лимит удалён.') }
    catch (failure) { setError(getApiError(failure, 'Не удалось удалить лимит.')) }
    finally { await refresh(); submitting.current = false; setBusy(false) }
  }

  return <section className="transactions-list-card budgets-panel" aria-labelledby="budgets-title">
    <div className="budgets-header"><h2 id="budgets-title">Бюджеты на месяц</h2>
      <label>Месяц бюджета<input type="month" min="1900-01" max="9998-12" value={month} disabled={busy}
        onChange={event => { setMonth(event.target.value); clear(); setError(''); setNotice('') }} /></label></div>
    {notice && <div className="notice success" role="status">{notice}</div>}
    {error && <div className="notice error" role="alert">{error}</div>}
    {!month && <p>Выберите месяц бюджета.</p>}
    {month && budgets.isPending && <DataSkeleton label="Загружаем бюджеты…" rows={2} />}
    {budgets.isError && <div className="notice error" role="alert">{getApiError(budgets.error, 'Не удалось загрузить бюджеты.')} <button className="text-button" onClick={() => void budgets.refetch()}>Повторить</button></div>}
    {budgets.isSuccess && budgets.data.length === 0 && <p>Задайте лимит для категории, чтобы следить за расходами.</p>}
    <ul className={`budget-list${budgets.data ? ' data-reveal' : ''}`}>{budgets.data?.map(budget => <li key={budget.id} className="budget-card">
      <div className="budget-heading"><strong>{budget.categoryName}{!budget.categoryActive ? ' · Неактивна' : ''}</strong><span>Лимит {money(budget.limit)}</span></div>
      <progress aria-label={`Расходы: ${budget.categoryName}`} max={Number(budget.limit)} value={Math.min(Number(budget.spent), Number(budget.limit))} />
      <div className="budget-heading"><span>Потрачено {money(budget.spent)}</span>
        {budget.overspent !== '0.00' ? <strong className="expense">Превышение {money(budget.overspent)}</strong> : <strong>Осталось {money(budget.remaining)}</strong>}</div>
      <div className="budget-actions"><button className="text-button" disabled={busy} onClick={() => { setEditing(budget); setLimit(budget.limit); setError(''); setNotice('') }}>Изменить лимит</button>
        <button className="text-button danger-action" disabled={busy} onClick={() => void remove(budget)}>Убрать лимит</button></div>
    </li>)}</ul>
    {(editing || available.length > 0) && <details className="panel-disclosure" open={editing ? true : undefined}>
      <summary>{editing ? `Изменить: ${editing.categoryName}` : 'Добавить лимит'}</summary>
      <form className="budget-form" noValidate onSubmit={event => void submit(event)}>
        {!editing && <label>Категория бюджета<select value={selected} disabled={busy} onChange={event => setCategoryId(event.target.value)}>
          {available.map(c => <option key={c.id} value={c.id}>{c.name}{!c.isActive ? ' · Неактивна' : ''}</option>)}</select></label>}
        <label>Лимит ({user?.baseCurrency ?? 'RUB'})<input value={limit} inputMode="decimal" placeholder="0,00" disabled={busy} onChange={event => setLimit(event.target.value)} /></label>
        <button className="primary-button" disabled={busy || !budgets.isSuccess}>{busy ? 'Сохраняем…' : 'Сохранить лимит'}</button>
        {editing && <button type="button" className="ghost-button" disabled={busy} onClick={clear}>Отменить редактирование</button>}
      </form></details>}
    {categories.isError && <p role="alert">Не удалось загрузить категории. <button className="text-button" onClick={() => void categories.refetch()}>Повторить</button></p>}
    {categories.isSuccess && !categories.data.some(c => c.type === 'Expense') && <p><Link to="/categories">Создайте расходную категорию</Link>, чтобы задать лимит.</p>}
  </section>
}

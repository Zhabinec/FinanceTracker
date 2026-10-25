import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { CurrencySelector } from '../currencies/CurrencySelector'
import { useCurrencyDisplay } from '../currencies/currencyDisplayContext'
import { useAuth } from '../auth/useAuth'
import { useAccounts, type MoneyAccount } from './accountApi'
import { openingBalanceSchema } from './validation'

function AccountCard({ account, onChange, busy }: {
  account: MoneyAccount; busy: boolean
  onChange: (account: MoneyAccount, name: string, isArchived: boolean) => Promise<void>
}) {
  const display = useCurrencyDisplay()
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(account.name)
  const [version, setVersion] = useState(account.version)
  const [error, setError] = useState('')
  const change = async (nextName: string, archived: boolean, expectedVersion: string) => {
    setError('')
    try { await onChange({ ...account, version: expectedVersion }, nextName, archived); setEditing(false) }
    catch (failure) { setError(getApiError(failure, 'Не удалось сохранить счёт.')) }
  }
  return <article className="transactions-list-card account-card">
    <h2>{account.name}</h2>
    <p>{account.currency} · {account.isArchived ? 'В архиве' : 'Активен'}</p>
    <strong className="summary-value">{display.money(account.balance, account.currency)}</strong>
    <p className="dashboard-note">Начальный остаток: {display.money(account.openingBalance, account.currency)}</p>
    {error && <div className="notice error" role="alert">{error}</div>}
    {editing ? <form onSubmit={event => { event.preventDefault(); void change(name, account.isArchived, version) }}>
      <label>Название счёта<input value={name} required maxLength={100} onChange={event => setName(event.target.value)} /></label>
      <div className="category-actions"><button className="text-button" disabled={busy}>Сохранить</button>
        <button type="button" className="text-button" disabled={busy} onClick={() => setEditing(false)}>Отмена</button></div>
    </form> : <div className="category-actions">
      <button className="text-button" disabled={busy} onClick={() => { setName(account.name); setVersion(account.version); setError(''); setEditing(true) }}>Переименовать</button>
      <button className="text-button" disabled={busy} onClick={() => void change(account.name, !account.isArchived, account.version)}>
        {account.isArchived ? 'Восстановить' : 'В архив'}</button>
      <Link className="text-button" to={`/transactions?accountId=${account.id}`}>Операции счёта</Link>
    </div>}
  </article>
}

export function AccountsPage() {
  const { user } = useAuth()
  const query = useAccounts()
  const client = useQueryClient()
  const [name, setName] = useState('')
  const [opening, setOpening] = useState('0')
  const [error, setError] = useState('')
  const [showArchive, setShowArchive] = useState(false)
  const refresh = () => client.invalidateQueries({ queryKey: ['accounts'] })
  const create = useMutation({
    mutationFn: (input: { name: string; openingBalance: string }) => http.post('/accounts', input), onSuccess: refresh,
  })
  const change = useMutation({
    mutationFn: (input: { account: MoneyAccount; name: string; isArchived: boolean }) =>
      http.put(`/accounts/${input.account.id}`, { name: input.name, isArchived: input.isArchived, version: input.account.version }),
    onSuccess: refresh, onError: refresh,
  })
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError('')
    const parsed = openingBalanceSchema.safeParse(opening)
    if (!parsed.success) { setError(parsed.error.issues[0].message); return }
    try { await create.mutateAsync({ name, openingBalance: parsed.data }); setName(''); setOpening('0') }
    catch (failure) { setError(getApiError(failure, 'Не удалось создать счёт.')) }
  }
  return <main className="dashboard-shell">
    <header className="app-header"><div><p className="eyebrow">Finance Tracker</p><h1>Счета</h1><p>{user?.email}</p></div>
      <nav className="header-actions"><Link className="ghost-button link-button" to="/">Обзор</Link>
        <Link className="ghost-button link-button" to="/transactions">Операции</Link>
        <Link className="ghost-button link-button" to="/categories">Категории</Link></nav>
    </header>
    <CurrencySelector />
    <section className="transactions-list-card"><h2>Новый счёт</h2>
      <p>Счета пока работают в вашей базовой валюте: {user?.baseCurrency}. Начальный остаток задаётся при создании; дальнейшие изменения записывайте операциями.</p>
      {error && <div className="notice error" role="alert">{error}</div>}
      <form onSubmit={event => void submit(event)} className="account-create-form">
        <label>Название<input required maxLength={100} placeholder="Карта, наличные, накопления" value={name} onChange={event => setName(event.target.value)} /></label>
        <label>Начальный остаток<input required inputMode="decimal" maxLength={40} value={opening} onChange={event => setOpening(event.target.value)} /></label>
        <button className="primary-button" disabled={create.isPending}>{create.isPending ? 'Создаём…' : 'Создать счёт'}</button>
      </form>
    </section>
    <p className="dashboard-note">Баланс включает начальный остаток и всю историю операций, включая будущие даты. Отрицательный баланс допустим. Архивирование сохраняет историю.</p>
    <label className="archive-toggle"><input type="checkbox" checked={showArchive} onChange={event => setShowArchive(event.target.checked)} /> Показать архивные счета</label>
    {query.isPending && <p role="status">Загружаем счета…</p>}
    {query.isError && <div className="notice error">{getApiError(query.error, 'Не удалось загрузить счета.')}</div>}
    <section className="summary-grid">{query.data?.filter(a => showArchive || !a.isArchived).map(account =>
      <AccountCard key={account.id} account={account} busy={change.isPending}
        onChange={async (a, nextName, isArchived) => { await change.mutateAsync({ account: a, name: nextName, isArchived }) }} />)}</section>
    {query.isSuccess && !query.data.some(a => showArchive || !a.isArchived) && <p>Нет активных счетов. Создайте новый или восстановите счёт из архива.</p>}
  </main>
}

import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import type { Category } from './categoryApi'
import type { MoneyAccount } from '../accounts/accountApi'
import { createTransaction, type FinanceTransaction } from '../transactions/transactionApi'
import { chooseQuickAccount, quickEntrySchema } from '../transactions/quickEntry'
import { getApiError } from '../../api/apiError'

export function QuickTransactionDialog({ category, accounts, accountsPending, accountsError, userId, onClose, onSaved, onRetryAccounts }: {
  category: Category; accounts: MoneyAccount[]; accountsPending: boolean; accountsError: boolean; userId: string
  onClose: () => void; onSaved: (transaction: FinanceTransaction) => void
  onRetryAccounts: () => void
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const nameRef = useRef<HTMLInputElement>(null)
  const submitting = useRef(false)
  const client = useQueryClient()
  const [name, setName] = useState('')
  const [amount, setAmount] = useState('')
  const [accountId, setAccountId] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [fields, setFields] = useState<Record<string, string>>({})
  const storageKey = `finance.quickAccount.${userId}`
  let preferredId: string | null = null
  try { preferredId = localStorage.getItem(storageKey) } catch { /* A preference is optional. */ }
  const active = accounts.filter(account => !account.isArchived)
  const selectedId = accountId ?? chooseQuickAccount(accounts, preferredId, userId)?.id ?? ''
  const selected = active.find(account => account.id === selectedId)
  const mutation = useMutation({ mutationFn: createTransaction,
    onSuccess: () => Promise.all(['transactions', 'dashboard', 'accounts'].map(key => client.invalidateQueries({ queryKey: [key] }))),
  })

  useEffect(() => {
    const dialog = dialogRef.current!
    const opener = document.activeElement
    dialog.showModal()
    nameRef.current?.focus()
    return () => { dialog.close(); if (opener instanceof HTMLElement) opener.focus() }
  }, [])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting.current) return
    setError(''); setFields({})
    const parsed = quickEntrySchema.safeParse({ amount, description: name })
    if (!parsed.success) {
      setFields(Object.fromEntries(parsed.error.issues.map(issue => [issue.path[0], issue.message])))
      return
    }
    if (!selected) { setError('Выберите активный счёт.'); return }
    submitting.current = true
    try {
      const transaction = await mutation.mutateAsync({ categoryId: category.id, type: category.type,
        accountId: selected.id, amount: parsed.data.amount.replace(',', '.'), description: parsed.data.description })
      try { localStorage.setItem(storageKey, selected.id) } catch { /* Saving the operation does not depend on storage. */ }
      onSaved(transaction)
    } catch (failure) {
      setError(getApiError(failure, 'Не удалось добавить операцию. Проверьте историю перед повторной отправкой, если связь прервалась.'))
      void client.invalidateQueries({ queryKey: ['accounts'] })
      void client.invalidateQueries({ queryKey: ['categories'] })
    } finally { submitting.current = false }
  }

  return <dialog ref={dialogRef} className="quick-dialog" aria-labelledby="quick-entry-title"
    onCancel={event => { event.preventDefault(); if (!submitting.current) onClose() }}
    onClick={event => {
      if (event.target !== event.currentTarget || submitting.current) return
      const box = event.currentTarget.getBoundingClientRect()
      if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) onClose()
    }}>
    <div className="quick-dialog-heading"><div><p className="eyebrow">{category.type === 'Expense' ? 'Новый расход' : 'Новый доход'}</p>
      <h2 id="quick-entry-title">{category.name}</h2></div>
      <button type="button" className="quick-close" aria-label="Закрыть" disabled={mutation.isPending} onClick={onClose}>×</button></div>
    <p className="dashboard-note">Дата — сегодня по Москве. Сумма в {selected?.currency ?? 'валюте счёта'}.</p>
    <form onSubmit={event => void submit(event)} noValidate>
      {error && <div className="notice error" role="alert">{error}</div>}
      <label>Название<input ref={nameRef} placeholder={category.type === 'Expense' ? 'Например, Чипсы' : 'Например, Подработка'}
        value={name} maxLength={500} onChange={event => { setName(event.target.value); setFields(previous => ({ ...previous, description: '' })) }} aria-invalid={Boolean(fields.description)} /></label>
      {fields.description && <p className="field-error" role="alert">{fields.description}</p>}
      <label>Сумма ({selected?.currency ?? '—'})<input inputMode="decimal" placeholder="25,00" value={amount}
        onChange={event => { setAmount(event.target.value); setFields(previous => ({ ...previous, amount: '' })) }} aria-invalid={Boolean(fields.amount)} /></label>
      {fields.amount && <p className="field-error" role="alert">{fields.amount}</p>}
      {active.length > 1 || (!selected && active.length > 0) ? <details className="quick-account"><summary>Счёт: {selected?.name ?? 'выберите счёт'}</summary>
        <label>Счёт<select value={selectedId} onChange={event => setAccountId(event.target.value)}>
          {!selected && <option value={selectedId}>Счёт недоступен</option>}
          {active.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
        </select></label></details> : selected && <p className="dashboard-note">Счёт: {selected.name}</p>}
      {accountsPending && <p role="status">Загружаем счёт…</p>}
      {accountsError && <p className="notice error" role="alert">Не удалось загрузить счета. <button type="button" className="text-button" onClick={onRetryAccounts}>Повторить загрузку</button></p>}
      {!accountsPending && !accountsError && !active.length && <p>Нет активных счетов. <Link to="/accounts" onClick={onClose}>Создать или восстановить счёт</Link></p>}
      <button className="primary-button" disabled={mutation.isPending || accountsPending || accountsError || !selected}>
        {mutation.isPending ? 'Добавляем…' : category.type === 'Expense' ? 'Добавить расход' : 'Добавить доход'}</button>
    </form>
  </dialog>
}

import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { getApiError } from '../../api/apiError'
import { formatDate } from '../../shared/formatting'
import { useCurrencyDisplay } from '../currencies/currencyDisplayContext'
import type { Category } from '../categories/categoryApi'
import type { MoneyAccount } from '../accounts/accountApi'
import type { FinanceTransaction, TransactionInput } from './transactionApi'
import { toTransactionInput, transactionSchema, type TransactionForm } from './validation'

export function TransactionRow({
  transaction,
  categories,
  accounts,
  currency,
  busy,
  onUpdate,
  onDelete,
}: {
  transaction: FinanceTransaction
  categories: Category[]
  accounts: MoneyAccount[]
  currency: string
  busy: boolean
  onUpdate: (transactionId: string, input: TransactionInput) => Promise<void>
  onDelete: (transactionId: string, version: string) => Promise<void>
}) {
  const display = useCurrencyDisplay()
  const [editing, setEditing] = useState(false)
  const [editingVersion, setEditingVersion] = useState(transaction.version)
  const [actionError, setActionError] = useState('')
  const { register, handleSubmit, control, setValue, setError, reset, formState: { errors } } =
    useForm<TransactionForm>({
      defaultValues: {
        accountId: transaction.accountId,
        categoryId: transaction.categoryId,
        type: transaction.type,
        amount: String(transaction.amount),
        description: transaction.description ?? '',
        occurredOn: transaction.occurredOn,
      },
    })
  const selectedType = useWatch({ control, name: 'type' })
  const selectableCategories = categories.filter((category) =>
    category.type === selectedType && (category.isActive || category.id === transaction.categoryId))

  const cancel = () => {
    reset({
      accountId: transaction.accountId,
      categoryId: transaction.categoryId,
      type: transaction.type,
      amount: String(transaction.amount),
      description: transaction.description ?? '',
      occurredOn: transaction.occurredOn,
    })
    setActionError('')
    setEditing(false)
  }

  const submit = handleSubmit(async (values) => {
    setActionError('')
    const parsed = transactionSchema.safeParse(values)
    if (!parsed.success) {
      for (const issue of parsed.error.issues)
        setError(issue.path[0] as keyof TransactionForm, { message: issue.message })
      return
    }

    try {
      await onUpdate(transaction.id, { ...toTransactionInput(parsed.data), version: editingVersion })
      setEditing(false)
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось сохранить операцию.'))
    }
  })

  const remove = async () => {
    if (!window.confirm('Удалить эту операцию без возможности восстановления?')) return
    setActionError('')
    try {
      await onDelete(transaction.id, transaction.version)
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось удалить операцию.'))
    }
  }

  if (editing) {
    return (
      <li className="transaction-row editing">
        <form className="transaction-edit-form" onSubmit={submit} noValidate>
          {actionError && <div className="notice error form-wide">{actionError}</div>}
          <label>Счёт<select {...register('accountId')}>
            {accounts.filter(a => !a.isArchived || a.id === transaction.accountId).map(a => <option key={a.id} value={a.id}>{a.name}{a.isArchived ? ' (архив)' : ''}</option>)}
          </select>{errors.accountId && <span className="field-error">{errors.accountId.message}</span>}</label>
          <label>Тип<select {...register('type')} onChange={(event) => {
            const type = event.target.value as TransactionForm['type']
            setValue('type', type)
            setValue('categoryId', '')
          }}><option value="Expense">Расход</option><option value="Income">Доход</option></select></label>
          <label>Категория<select {...register('categoryId')}>
            <option value="">Выберите</option>
            {selectableCategories.map((category) => <option key={category.id} value={category.id}>
              {category.name}{category.isActive ? '' : ' (неактивна)'}
            </option>)}
          </select>{errors.categoryId && <span className="field-error">{errors.categoryId.message}</span>}</label>
          <label>Сумма ({currency})<input inputMode="decimal" {...register('amount')} />
            {errors.amount && <span className="field-error">{errors.amount.message}</span>}</label>
          <label>Дата<input type="date" {...register('occurredOn')} />
            {errors.occurredOn && <span className="field-error">{errors.occurredOn.message}</span>}</label>
          <label className="form-wide">Описание<input maxLength={500} {...register('description')} />
            {errors.description && <span className="field-error">{errors.description.message}</span>}</label>
          <div className="transaction-edit-actions form-wide">
            <button className="primary-button" disabled={busy}>Сохранить</button>
            <button type="button" className="ghost-button" disabled={busy} onClick={cancel}>Отмена</button>
          </div>
        </form>
      </li>
    )
  }

  const amount = display.money(transaction.amount, currency)
  const date = formatDate(transaction.occurredOn)

  return (
    <li className="transaction-row">
      <div className="transaction-main">
        <span className={`transaction-sign ${transaction.type.toLowerCase()}`} aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
            <path d={transaction.type === 'Income' ? 'M5 12h14M12 5v14' : 'M5 12h14'} />
          </svg>
        </span>
        <div><strong>{transaction.description || transaction.categoryName}</strong>
          <span>{transaction.categoryName} · {accounts.find(a => a.id === transaction.accountId)?.name ?? 'Счёт'}</span>
          <time dateTime={transaction.occurredOn}>{date}</time></div>
      </div>
      <strong className={`transaction-amount ${transaction.type.toLowerCase()}`}>
        {transaction.type === 'Income' ? '+' : '−'}{amount}
      </strong>
      <div className="category-actions">
        <button type="button" className="text-button" disabled={busy}
          onClick={() => { cancel(); setEditingVersion(transaction.version); setEditing(true) }}>Изменить</button>
        <button type="button" className="text-button danger-action" disabled={busy}
          onClick={() => { void remove() }}>Удалить</button>
      </div>
      {actionError && <div className="notice error transaction-row-error">{actionError}</div>}
    </li>
  )
}

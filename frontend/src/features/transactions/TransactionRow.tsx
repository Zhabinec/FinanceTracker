import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { getApiError } from '../../api/apiError'
import { formatMoney, formatDate } from '../../shared/formatting'
import type { Category } from '../categories/categoryApi'
import type { FinanceTransaction, TransactionInput } from './transactionApi'
import { toTransactionInput, transactionSchema, type TransactionForm } from './validation'

export function TransactionRow({
  transaction,
  categories,
  currency,
  busy,
  onUpdate,
  onDelete,
}: {
  transaction: FinanceTransaction
  categories: Category[]
  currency: string
  busy: boolean
  onUpdate: (transactionId: string, input: TransactionInput) => Promise<void>
  onDelete: (transactionId: string) => Promise<void>
}) {
  const [editing, setEditing] = useState(false)
  const [actionError, setActionError] = useState('')
  const { register, handleSubmit, control, setValue, setError, reset, formState: { errors } } =
    useForm<TransactionForm>({
      defaultValues: {
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
      await onUpdate(transaction.id, toTransactionInput(parsed.data))
      setEditing(false)
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось сохранить операцию.'))
    }
  })

  const remove = async () => {
    if (!window.confirm('Удалить эту операцию без возможности восстановления?')) return
    setActionError('')
    try {
      await onDelete(transaction.id)
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось удалить операцию.'))
    }
  }

  if (editing) {
    return (
      <li className="transaction-row editing">
        <form className="transaction-edit-form" onSubmit={submit} noValidate>
          {actionError && <div className="notice error form-wide">{actionError}</div>}
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
          <label>Сумма<input inputMode="decimal" {...register('amount')} />
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

  const amount = formatMoney(transaction.amount, currency)
  const date = formatDate(transaction.occurredOn)

  return (
    <li className="transaction-row">
      <div className="transaction-main">
        <span className={`transaction-sign ${transaction.type.toLowerCase()}`}>
          {transaction.type === 'Income' ? '+' : '−'}
        </span>
        <div><strong>{transaction.categoryName}</strong>
          <span>{transaction.description || 'Без описания'} · {date}</span></div>
      </div>
      <strong className={`transaction-amount ${transaction.type.toLowerCase()}`}>
        {transaction.type === 'Income' ? '+' : '−'}{amount}
      </strong>
      <div className="category-actions">
        <button type="button" className="text-button" disabled={busy}
          onClick={() => setEditing(true)}>Изменить</button>
        <button type="button" className="text-button danger-action" disabled={busy}
          onClick={() => { void remove() }}>Удалить</button>
      </div>
      {actionError && <div className="notice error transaction-row-error">{actionError}</div>}
    </li>
  )
}

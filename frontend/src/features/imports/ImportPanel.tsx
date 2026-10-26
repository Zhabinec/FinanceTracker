import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { useAccounts } from '../accounts/accountApi'
import { csvHeaders } from './csvHeaders'

type Mapping = { date: number; amount: number; category: number; description: number | null; type: number | null }
type Preview = { rows: { row: number; date: string; amount: string; category: string; type: string; description: string; errors: string[] }[];
  canApply: boolean; alreadyImported: boolean; currency: string }

export function ImportPanel() {
  const client = useQueryClient()
  const accounts = useAccounts()
  const active = accounts.data?.filter(a => !a.isArchived) ?? []
  const [accountId, setAccountId] = useState('')
  const [content, setContent] = useState('')
  const [fileName, setFileName] = useState('')
  const [delimiter, setDelimiter] = useState(';')
  const [defaultType, setDefaultType] = useState('Expense')
  const [mapping, setMapping] = useState<Mapping>({ date: 0, amount: 1, category: 2, description: null, type: null })
  const [preview, setPreview] = useState<Preview | null>(null)
  const [busy, setBusy] = useState(false)
  const submitting = useRef(false)
  const fileRead = useRef(0)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const headers = csvHeaders(content, delimiter)
  const resetPreview = () => { setPreview(null); setNotice(''); setError('') }
  const input = { content, delimiter, accountId: accountId || active[0]?.id || '', mapping, defaultType }
  const execute = async (apply: boolean) => {
    if (submitting.current) return
    submitting.current = true; setBusy(true); setError(''); setNotice('')
    try {
      if (!apply) setPreview((await http.post<Preview>('/imports/csv/preview', input)).data)
      else {
        const result = (await http.post<{ count: number }>('/imports/csv/apply', input)).data
        setNotice(`Импортировано операций: ${result.count}.`)
        setPreview(null); setContent(''); setFileName('')
        await Promise.all(['transactions', 'accounts', 'dashboard', 'budgets'].map(key => client.invalidateQueries({ queryKey: [key] })))
      }
    } catch (failure) { setError(getApiError(failure, 'Не удалось выполнить импорт.')); if (apply) setPreview(null) }
    finally { submitting.current = false; setBusy(false) }
  }
  return <details className="panel-disclosure import-panel"><summary>Импорт из CSV</summary>
    <p className="dashboard-note">Экспортируйте Excel или другую программу в CSV. Файл должен содержать заголовки, дату, положительную сумму и название вашей категории. Все суммы — в валюте выбранного счёта. До сохранения вы увидите предпросмотр.</p>
    <a className="text-button" href="/import-example.csv" download>Скачать пример CSV</a>
    {error && <div className="notice error" role="alert">{error}</div>}
    {notice && <div className="notice success" role="status">{notice}</div>}
    <div className="import-form">
      <label>CSV-файл<input type="file" accept=".csv,.tsv,text/csv,text/tab-separated-values" disabled={busy} onChange={async event => {
        const sequence = ++fileRead.current
        const file = event.target.files?.[0]
        resetPreview(); setContent(''); setFileName('')
        if (!file) return
        if (file.size > 2_000_000) { setError('Файл не больше 2 МБ и 1000 операций.'); return }
        try {
          const bytes = await file.arrayBuffer()
          let text: string
          try { text = new TextDecoder('utf-8', { fatal: true }).decode(bytes) }
          catch { text = new TextDecoder('windows-1251').decode(bytes) }
          if (sequence !== fileRead.current) return
          if (text.length > 1_000_000) { setError('Не больше 1 000 000 символов.'); return }
          setContent(text); setFileName(file.name)
        } catch { if (sequence === fileRead.current) setError('Не удалось прочитать файл.') }
      }} /></label>
      <label>Разделитель CSV<select value={delimiter} disabled={busy} onChange={event => { setDelimiter(event.target.value); resetPreview() }}>
        <option value=";">Точка с запятой (;)</option><option value=",">Запятая (,)</option><option value={'\t'}>Табуляция</option></select></label>
      <label>Счёт для импорта<select value={input.accountId} disabled={busy || accounts.isPending} onChange={event => { setAccountId(event.target.value); resetPreview() }}>
        {!active.some(a => a.id === input.accountId) && <option value={input.accountId}>Выберите активный счёт</option>}
        {active.map(a => <option key={a.id} value={a.id}>{a.name} ({a.currency})</option>)}</select></label>
      <label>Тип без колонки<select value={defaultType} disabled={busy || mapping.type !== null} onChange={event => { setDefaultType(event.target.value); resetPreview() }}>
        <option value="Expense">Расход</option><option value="Income">Доход</option></select></label>
      {content && (Object.entries({ date: 'Колонка даты', amount: 'Колонка суммы', category: 'Колонка категории', description: 'Колонка описания', type: 'Колонка типа' }) as [keyof Mapping, string][]).map(([key, title]) =>
        <label key={key}>{title}<select value={mapping[key] ?? ''} disabled={busy} onChange={event => { setMapping(previous => ({ ...previous, [key]: event.target.value === '' ? null : Number(event.target.value) })); resetPreview() }}>
          {(key === 'description' || key === 'type') && <option value="">Не использовать</option>}
          {headers.map((header, index) => <option key={index} value={index}>{index + 1}. {header || 'Без названия'}</option>)}</select></label>)}
    </div>
    {accounts.isError && <p role="alert">Не удалось загрузить счета. <button className="text-button" onClick={() => void accounts.refetch()}>Повторить</button></p>}
    <button className="ghost-button" disabled={busy || !content || !active.some(a => a.id === input.accountId)} onClick={() => void execute(false)}>{busy ? 'Обрабатываем…' : 'Предпросмотр CSV'}</button>
    {preview && <section aria-label="Предпросмотр CSV" className="import-preview">
      <h3>{fileName} · {preview.rows.length} операций · {preview.currency}</h3>
      <p className="dashboard-note">Дата: ГГГГ-ММ-ДД, ДД.ММ.ГГГГ или ДД/ММ/ГГГГ. Тип: Доход/Расход или Income/Expense. Неизвестную категорию нужно сначала создать в разделе «Категории».</p>
      {preview.alreadyImported && <div className="notice error" role="alert">Этот CSV уже импортирован. Повторная загрузка заблокирована.</div>}
      {!preview.canApply && !preview.alreadyImported && <div className="notice error" role="alert">Есть ошибки. Исправьте файл или колонки и повторите предпросмотр. Ничего не сохранено.</div>}
      <ol className="import-rows">{preview.rows.map(row => <li key={row.row} className={row.errors.length ? 'invalid' : ''}>
        <strong>Строка {row.row} · {row.category}</strong><span>{row.date} · {row.type === 'Income' ? 'Доход' : 'Расход'} · {row.amount} {preview.currency}</span>
        {row.description && <span>{row.description}</span>}{row.errors.map(message => <span className="field-error" key={message}>{message}</span>)}</li>)}</ol>
      <button className="primary-button" disabled={busy || !preview.canApply} onClick={() => void execute(true)}>Импортировать {preview.rows.length} операций</button>
    </section>}
  </details>
}

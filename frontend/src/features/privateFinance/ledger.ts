import type { FinancialDocument } from './model'
import { fingerprint } from './crypto.ts'
import { todayInMoscow } from '../transactions/validation.ts'

export class FinanceError extends Error {
  status: number
  constructor(message: string, status = 400) { super(message); this.status = status }
}
const fail = (message: string, status = 400): never => { throw new FinanceError(message, status) }
const MAX = 999999999999999999n
export function cents(value: unknown, signed = false): bigint {
  if (typeof value !== 'string' || !(signed ? /^-?\d+(?:\.\d{1,2})?$/ : /^\d+(?:\.\d{1,2})?$/).test(value)) return fail('Сумма должна быть строкой с точностью до копеек.')
  const negative = value.startsWith('-'), [whole, fraction = ''] = value.replace(/^-/, '').split('.')
  const result = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'))
  if (result > MAX || (!signed && result === 0n)) return fail('Сумма должна быть положительной и помещаться в decimal(18,2).')
  return negative ? -result : result
}
export function decimal(value: bigint) { const sign = value < 0n ? '-' : ''; const n = value < 0n ? -value : value; return `${sign}${n / 100n}.${String(n % 100n).padStart(2, '0')}` }
const text = (value: unknown, max = 100) => typeof value === 'string' && value.trim().length > 0 && value.trim().length <= max ? value.trim() : fail(`Введите название длиной от 1 до ${max} символов.`)
const description = (value: unknown) => value == null || value === '' ? null : typeof value === 'string' && value.trim().length <= 500 ? value.trim() || null : fail('Описание не должно превышать 500 символов.')
const kind = (value: unknown): 'Income' | 'Expense' => value === 'Income' || value === 'Expense' ? value : fail('Выберите доход или расход.')
function day(value: unknown) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return fail('Введите дату в формате ГГГГ-ММ-ДД.')
  const date = new Date(`${value}T00:00:00Z`), year = Number(value.slice(0, 4))
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value && year >= 1900 && year <= 9998 ? value : fail('Некорректная дата.')
}
function record(value: unknown): Record<string, unknown> { return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {} }
const uuid = (value: unknown) => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value) ? value : fail('Некорректный идентификатор.')
function page<T>(items: T[], params: Record<string, unknown>) {
  const p = Number(params.page ?? 1), size = Number(params.pageSize ?? 20)
  if (!Number.isInteger(p) || p < 1 || p > 1_000_000 || !Number.isInteger(size) || size < 1 || size > 100) return fail('Некорректная страница.')
  return { items: items.slice((p - 1) * size, p * size), totalCount: items.length, page: p, pageSize: size }
}
function version(item: { version: string }, requested: unknown) { if (item.version !== requested) fail('Запись уже изменена. Обновите данные перед сохранением.', 409) }
function category(data: FinancialDocument, id: unknown) { return data.categories.find(c => c.id === id) ?? fail('Категория не найдена.', 404) }
function account(data: FinancialDocument, id: unknown) { return data.accounts.find(a => a.id === id) ?? fail('Счёт не найден.', 404) }
function balance(data: FinancialDocument, id: string) {
  let total = cents(account(data, id).openingBalance, true)
  for (const t of data.transactions) if (t.accountId === id) total += (t.type === 'Income' ? 1n : -1n) * cents(t.amount)
  for (const t of data.transfers) if (!t.cancelledAtUtc) total += (t.targetAccountId === id ? cents(t.amount) : 0n) - (t.sourceAccountId === id ? cents(t.amount) : 0n)
  return decimal(total)
}
const transactionDto = (data: FinancialDocument, t: FinancialDocument['transactions'][number]) => ({ ...t, categoryName: category(data, t.categoryId).name })
const transferDto = (data: FinancialDocument, t: FinancialDocument['transfers'][number]) => ({ ...t, sourceAccountName: account(data, t.sourceAccountId).name, targetAccountName: account(data, t.targetAccountId).name })
function filtered(data: FinancialDocument, params: Record<string, unknown>) {
  const from = params.from ? day(params.from) : '', to = params.to ? day(params.to) : ''
  if (from && to && from > to) fail('Начало периода позже конца.')
  return data.transactions.filter(t => (!from || t.occurredOn >= from) && (!to || t.occurredOn <= to) &&
    (!params.categoryId || t.categoryId === params.categoryId) && (!params.accountId || t.accountId === params.accountId) && (!params.type || t.type === params.type))
}
export async function executeFinance(data: FinancialDocument, method: string, path: string, input: unknown = {}, query: unknown = {}, ownerId = ''): Promise<unknown> {
  const body = record(input), params = record(query), [, area, id, action] = path.split('/')
  const now = new Date().toISOString(), newId = () => crypto.randomUUID()
  if (area === 'accounts') {
    if (method === 'get') return [...data.accounts].sort((a, b) => a.name.localeCompare(b.name, 'ru')).map(a => ({ ...a, balance: balance(data, a.id) }))
    const name = text(body.name)
    if (data.accounts.some(a => a.id !== id && a.name.toUpperCase() === name.toUpperCase())) return fail('Счёт с таким названием уже существует.', 409)
    if (method === 'post') {
      const item = { id: newId(), name, openingBalance: decimal(cents(body.openingBalance ?? '0', true)), currency: data.baseCurrency, isArchived: false, version: newId() }
      data.accounts.push(item); return { ...item, balance: item.openingBalance }
    }
    if (method === 'put') { const item = account(data, id); version(item, body.version); if (typeof body.isArchived !== 'boolean') return fail('Некорректный статус счёта.'); Object.assign(item, { name, isArchived: body.isArchived, version: newId() }); return null }
  }
  if (area === 'categories') {
    if (method === 'get') return [...data.categories].sort((a, b) => a.name.localeCompare(b.name, 'ru'))
    if (method === 'post' && !id) {
      const name = text(body.name), type = kind(body.type)
      if (data.categories.some(c => c.type === type && c.name.toUpperCase() === name.toUpperCase())) return fail('Категория с таким названием уже существует.', 409)
      const item = { id: newId(), name, type, isActive: true, version: newId() }; data.categories.push(item); return item
    }
    const item = category(data, id)
    version(item, body.version)
    if (method === 'patch') {
      const name = text(body.name)
      if (data.categories.some(c => c.id !== id && c.type === item.type && c.name.toUpperCase() === name.toUpperCase())) return fail('Категория с таким названием уже существует.', 409)
      item.name = name; item.version = newId(); return item
    }
    if (method === 'post' && ['deactivate', 'restore'].includes(action)) { item.isActive = action === 'restore'; item.version = newId(); return item }
  }
  if (area === 'transactions') {
    if (method === 'get') {
      const sort = params.sort ?? 'dateDesc'
      if (!['dateDesc', 'dateAsc', 'amountDesc', 'amountAsc'].includes(String(sort))) return fail('Некорректная сортировка.')
      const items = filtered(data, params).sort((a, b) => {
        const comparison = String(sort).startsWith('amount') ? cents(a.amount) < cents(b.amount) ? -1 : cents(a.amount) > cents(b.amount) ? 1 : 0 : a.occurredOn.localeCompare(b.occurredOn)
        return (String(sort).endsWith('Desc') ? -comparison : comparison) || b.createdAtUtc.localeCompare(a.createdAtUtc) || a.id.localeCompare(b.id)
      })
      return page(items.map(t => transactionDto(data, t)), params)
    }
    if (method === 'delete') {
      const item = data.transactions.find(t => t.id === id) ?? fail('Запись не найдена.', 404); version(item, params.version)
      data.transactions = data.transactions.filter(t => t.id !== id); return null
    }
    const type = kind(body.type), amount = decimal(cents(body.amount)), note = description(body.description)
    const accountId = body.accountId ?? (data.accounts.find(a => a.id === ownerId)?.id ?? data.accounts[0]?.id)
    const intent = JSON.stringify({ categoryId: body.categoryId, type, amount, description: note, occurredOn: body.occurredOn ?? null, accountId })
    if (method === 'post' && body.requestId) {
      uuid(body.requestId)
      const receipt = data.requests.find(r => r.requestId === body.requestId)
      if (receipt) {
        if (receipt.fingerprint !== intent) return fail('Этот запрос уже использован для другой операции.', 409)
        const existing = data.transactions.find(t => t.id === receipt.transactionId) ?? fail('Запись уже удалена. Повтор не восстановит её.', 409)
        return transactionDto(data, existing)
      }
    }
    const old = method === 'put' ? data.transactions.find(t => t.id === id) ?? fail('Запись не найдена.', 404) : null
    if (old) version(old, body.version)
    const cat = category(data, body.categoryId), acc = account(data, accountId)
    if (cat.type !== type) return fail('Тип категории не совпадает с операцией.')
    if (!cat.isActive && old?.categoryId !== cat.id) return fail('Категория неактивна.', 409)
    if (acc.isArchived && old?.accountId !== acc.id) return fail('Архивный счёт не принимает новые операции.', 409)
    const item = { id: old?.id ?? newId(), accountId: acc.id, categoryId: cat.id, type, amount, description: note, occurredOn: day(body.occurredOn ?? todayInMoscow()), createdAtUtc: old?.createdAtUtc ?? now, version: newId() }
    if (old) Object.assign(old, item)
    else { data.transactions.push(item); if (body.requestId) data.requests.push({ requestId: String(body.requestId), transactionId: item.id, fingerprint: intent }) }
    return transactionDto(data, item)
  }
  if (area === 'transfers') {
    if (method === 'get') return page([...data.transfers].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc)).map(t => transferDto(data, t)), params)
    if (method === 'post' && action === 'cancel') {
      const item = data.transfers.find(t => t.id === id) ?? fail('Перевод не найден.', 404)
      if (!item.cancelledAtUtc) { version(item, body.version); item.cancelledAtUtc = now; item.version = newId() }
      return null
    }
    const requestId = uuid(body.requestId), source = account(data, body.sourceAccountId), target = account(data, body.targetAccountId)
    const amount = decimal(cents(body.amount)), note = description(body.description)
    const existing = data.transfers.find(t => t.requestId === requestId)
    if (existing) {
      if (existing.sourceAccountId !== source.id || existing.targetAccountId !== target.id || existing.amount !== amount || existing.description !== note) return fail('Запрос уже использован для другого перевода.', 409)
      return transferDto(data, existing)
    }
    if (source.id === target.id || source.currency !== target.currency || source.isArchived || target.isArchived) return fail('Нужны два разных активных счёта в одной валюте.', 409)
    const item = { id: newId(), requestId, sourceAccountId: source.id, targetAccountId: target.id, currency: source.currency, amount, description: note, occurredOn: todayInMoscow(), createdAtUtc: now, cancelledAtUtc: null, version: newId() }
    data.transfers.push(item); return transferDto(data, item)
  }
  if (area === 'budgets') {
    if (method === 'get') {
      const month = day(params.month)
      if (!month.endsWith('-01')) return fail('Укажите первый день месяца.')
      return data.budgets.filter(b => b.month === month).map(b => {
        const cat = category(data, b.categoryId), limit = cents(b.limit)
        const spent = data.transactions.filter(t => t.type === 'Expense' && t.categoryId === cat.id && t.occurredOn.startsWith(month.slice(0, 7))).reduce((sum, t) => sum + cents(t.amount), 0n)
        return { ...b, categoryName: cat.name, categoryActive: cat.isActive, spent: decimal(spent), remaining: decimal(limit > spent ? limit - spent : 0n), overspent: decimal(spent > limit ? spent - limit : 0n) }
      })
    }
    if (method === 'post') {
      const month = day(body.month), cat = category(data, body.categoryId), limit = decimal(cents(body.limit))
      if (!month.endsWith('-01') || cat.type !== 'Expense' || !cat.isActive) return fail('Выберите месяц и активную расходную категорию.')
      if (data.budgets.some(b => b.month === month && b.categoryId === cat.id)) return fail('Лимит на этот месяц уже есть.', 409)
      const item = { id: newId(), categoryId: cat.id, month, limit, version: newId() }; data.budgets.push(item); return item
    }
    const item = data.budgets.find(b => b.id === id) ?? fail('Бюджет не найден.', 404)
    version(item, method === 'delete' ? params.version : body.version)
    if (method === 'delete') { data.budgets = data.budgets.filter(b => b.id !== id); return null }
    if (method === 'put') { item.limit = decimal(cents(body.limit)); item.version = newId(); return item }
  }
  if (area === 'dashboard' && method === 'get') {
    const from = String(params.from ?? `${todayInMoscow().slice(0, 7)}-01`), to = String(params.to ?? todayInMoscow())
    const items = filtered(data, { from, to })
    const sum = (type: string) => items.filter(t => t.type === type).reduce((total, t) => total + cents(t.amount), 0n)
    const income = sum('Income'), expense = sum('Expense')
    const categories = data.categories.filter(c => items.some(t => t.categoryId === c.id)).map(c => ({ categoryId: c.id, categoryName: c.name, type: c.type, amount: decimal(items.filter(t => t.categoryId === c.id).reduce((total, t) => total + cents(t.amount), 0n)) }))
    return { from, to, income: decimal(income), expense: decimal(expense), result: decimal(income - expense), categories, recent: [...items].sort((a, b) => b.occurredOn.localeCompare(a.occurredOn) || b.createdAtUtc.localeCompare(a.createdAtUtc)).slice(0, 10).map(t => transactionDto(data, t)) }
  }
  if (area === 'imports' && id === 'csv') return importCsv(data, body, action === 'apply', now)
  return fail('Неподдерживаемое финансовое действие.', 404)
}

export function csvRows(content: string, delimiter: string) {
  if (!content.trim() || content.length > 1_000_000 || ![',', ';', '\t'].includes(delimiter)) return fail('Проверьте CSV: не больше 1 000 000 символов, разделитель — запятая, точка с запятой или табуляция.')
  const rows: string[][] = [], cells: string[] = []; let field = '', quoted = false, closed = false
  const finishCell = () => { cells.push(field); field = ''; closed = false; if (cells.length > 50) fail('Не больше 50 колонок.') }
  const finishRow = () => { finishCell(); if (cells.some(c => c.trim())) rows.push([...cells]); cells.length = 0; if (rows.length > 1001) fail('Не больше 1000 операций за импорт.') }
  const text = content.replace(/^\uFEFF+/, '')
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (quoted) { if (c === '"') { if (text[i + 1] === '"') { field += '"'; i++ } else { quoted = false; closed = true } } else field += c }
    else if (c === delimiter) finishCell()
    else if (c === '\r' || c === '\n') { if (c === '\r' && text[i + 1] === '\n') i++; finishRow() }
    else if (c === '"' && field.length === 0 && !closed) quoted = true
    else { if (closed || c === '"') fail('Некорректные кавычки в CSV.'); field += c }
  }
  if (quoted) return fail('В CSV не закрыты кавычки.')
  if (field.length || cells.length || closed) finishRow()
  if (rows.length < 2) return fail('Нужны заголовки и хотя бы одна операция.')
  return rows
}
async function importCsv(data: FinancialDocument, body: Record<string, unknown>, apply: boolean, now: string) {
  if (typeof body.content !== 'string' || typeof body.delimiter !== 'string') return fail('Выберите CSV-файл.')
  const rows = csvRows(body.content, body.delimiter), mapping = record(body.mapping), acc = account(data, body.accountId)
  if (acc.isArchived) return fail('Выберите активный счёт.')
  const columns = [mapping.date, mapping.amount, mapping.category, mapping.description, mapping.type].filter(c => c != null)
  if (columns.length < 3 || new Set(columns).size !== columns.length || columns.some(c => !Number.isInteger(c) || Number(c) < 0 || Number(c) >= rows[0].length)) return fail('Каждому полю нужна отдельная существующая колонка.')
  const hash = await fingerprint(body.content.replace(/^\uFEFF+/, '').replaceAll('\r\n', '\n').replaceAll('\r', '\n').replace(/\n+$/, ''))
  const alreadyImported = data.imports.includes(hash), defaultType = kind(body.defaultType)
  const preview = rows.slice(1).map((cells, index) => {
    const cell = (key: string) => mapping[key] == null ? '' : (cells[Number(mapping[key])] ?? '').trim()
    const errors: string[] = []; let date = cell('date'), amount = cell('amount'), type = defaultType
    if (cells.length !== rows[0].length) errors.push('Число колонок отличается от заголовка.')
    const match = /^(\d{2})[./](\d{2})[./](\d{4})$/.exec(date)
    if (match) date = `${match[3]}-${match[2]}-${match[1]}`
    try { date = day(date) } catch { errors.push('Проверьте дату.') }
    try { amount = decimal(cents(amount.replace(/[ \u00a0\u202f]/g, '').replace(',', '.'))) } catch { errors.push('Сумма должна быть положительной, не больше двух знаков после запятой.') }
    if (mapping.type != null) {
      const value = cell('type').toLowerCase()
      if (['income', 'доход'].includes(value)) type = 'Income'
      else if (['expense', 'расход'].includes(value)) type = 'Expense'
      else errors.push('Тип: Income/Доход или Expense/Расход.')
    }
    const name = cell('category'), cat = data.categories.find(c => c.type === type && c.name.toUpperCase() === name.toUpperCase())
    if (!cat || !cat.isActive) errors.push('Создайте или восстановите категорию с этим названием и типом.')
    const note = cell('description'); if (note.length > 500) errors.push('Описание: не больше 500 символов.')
    return { row: index + 2, date, amount, category: name, type, description: note, errors }
  })
  const canApply = !alreadyImported && preview.every(row => row.errors.length === 0)
  if (!apply) return { rows: preview, canApply, alreadyImported, currency: acc.currency }
  if (!canApply) return fail(alreadyImported ? 'Этот CSV уже импортирован.' : 'Исправьте ошибки. Ни одна запись не импортирована.', 409)
  for (const row of preview) {
    const cat = data.categories.find(c => c.name.toUpperCase() === row.category.toUpperCase() && c.type === row.type)!
    data.transactions.push({ id: crypto.randomUUID(), accountId: acc.id, categoryId: cat.id, type: row.type, amount: row.amount, description: row.description || null, occurredOn: row.date, createdAtUtc: now, version: crypto.randomUUID() })
  }
  data.imports.push(hash)
  return { count: preview.length }
}

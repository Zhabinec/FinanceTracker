import { z } from 'zod'

const id = z.uuid()
const money = z.string().regex(/^-?\d{1,16}\.\d{2}$/)
const positive = money.refine(value => !value.startsWith('-') && value !== '0.00')
const type = z.enum(['Income', 'Expense'])
const day = z.string().regex(/^\d{4}-\d{2}-\d{2}$/).refine(value => {
  const year = Number(value.slice(0, 4))
  const date = new Date(`${value}T00:00:00Z`)
  return year >= 1900 && year <= 9998 && Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
})
const instant = z.string().max(40).refine(value => Number.isFinite(Date.parse(value)))
export const documentSchema = z.object({
  format: z.literal(1), baseCurrency: z.string().regex(/^[A-Z]{3}$/),
  accounts: z.array(z.object({ id, name: z.string().min(1).max(100), currency: z.string().regex(/^[A-Z]{3}$/), openingBalance: money, isArchived: z.boolean(), version: id })).min(1).max(1000),
  categories: z.array(z.object({ id, name: z.string().min(1).max(100), type, isActive: z.boolean(), version: id.optional() }).transform(value => ({ ...value, version: value.version ?? value.id }))).max(2000),
  transactions: z.array(z.object({ id, accountId: id, categoryId: id, type, amount: positive, description: z.string().max(500).nullable(), occurredOn: day, createdAtUtc: instant, version: id })).max(20000),
  transfers: z.array(z.object({ id, requestId: id, sourceAccountId: id, targetAccountId: id, currency: z.string().regex(/^[A-Z]{3}$/), amount: positive, description: z.string().max(500).nullable(), occurredOn: day, createdAtUtc: instant, cancelledAtUtc: instant.nullable(), version: id })).max(10000),
  budgets: z.array(z.object({ id, categoryId: id, month: day.refine(value => value.endsWith('-01')), limit: positive, version: id })).max(10000),
  requests: z.array(z.object({ requestId: id, transactionId: id, fingerprint: z.string().max(10000) })).max(30000),
  imports: z.array(z.string().regex(/^[A-F0-9]{64}$/)).max(20000),
}).strict()
export type FinancialDocument = z.infer<typeof documentSchema>
export type VaultEnvelope = { id: string; revision: number; mutationId: string; nonce: string; ciphertext: string; isActive: boolean; updatedAtUtc: string; format: number }
export type VaultWrite = { id: string; expectedRevision: number; mutationId: string; nonce: string; ciphertext: string; format: number }

export function parseDocument(value: unknown): FinancialDocument {
  const parsed = documentSchema.safeParse(value)
  if (!parsed.success) throw new Error('Финансовое хранилище повреждено или использует неподдерживаемый формат.')
  const data = parsed.data
  for (const items of [data.accounts, data.categories, data.transactions, data.transfers, data.budgets])
    if (new Set(items.map(item => item.id)).size !== items.length) throw new Error('В хранилище повторяются идентификаторы.')
  const accounts = new Map(data.accounts.map(a => [a.id, a]))
  const categories = new Map(data.categories.map(c => [c.id, c]))
  if (data.transactions.some(t => !accounts.has(t.accountId) || categories.get(t.categoryId)?.type !== t.type) ||
    data.transfers.some(t => t.sourceAccountId === t.targetAccountId || accounts.get(t.sourceAccountId)?.currency !== t.currency || accounts.get(t.targetAccountId)?.currency !== t.currency) ||
    data.budgets.some(b => categories.get(b.categoryId)?.type !== 'Expense')) throw new Error('Нарушены связи финансовых записей.')
  return data
}

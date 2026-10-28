import { http } from '../../api/http'

export type Transfer = {
  id: string; sourceAccountId: string; sourceAccountName: string; targetAccountId: string; targetAccountName: string
  currency: string; amount: string; description: string | null; occurredOn: string; createdAtUtc: string
  cancelledAtUtc: string | null; version: string
}
export type TransferInput = { requestId: string; sourceAccountId: string; targetAccountId: string; amount: string; description: string | null }
export type TransferPage = { items: Transfer[]; totalCount: number; page: number; pageSize: number }
export async function getTransfers(page: number, signal?: AbortSignal) {
  return (await http.get<TransferPage>('/finance/transfers', { params: { page, pageSize: 10 }, signal })).data
}
export async function createTransfer(input: TransferInput) { return (await http.post<Transfer>('/finance/transfers', input)).data }
export async function cancelTransfer(transfer: Transfer) { await http.post(`/finance/transfers/${transfer.id}/cancel`, { version: transfer.version }) }

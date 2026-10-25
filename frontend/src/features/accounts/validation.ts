import { z } from 'zod'

export const openingBalanceSchema = z.string().trim()
  .regex(/^-?\d+(?:[.,]\d{1,2})?$/, 'Введите остаток с точностью до копеек, например 1500,50 или -100.')
  .refine(value => (value.replace('-', '').split(/[.,]/)[0].replace(/^0+/, '') || '0').length <= 16, 'Сумма слишком велика.')
  .transform(value => value.replace(',', '.'))

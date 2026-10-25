function decimalParts(value: string) {
  if (!/^-?\d+(?:\.\d+)?$/.test(value)) throw new Error('Invalid decimal string')
  const negative = value.startsWith('-')
  const [whole, fraction = ''] = value.replace(/^-/, '').split('.')
  return { value: BigInt(whole + fraction) * (negative ? -1n : 1n), scale: 10n ** BigInt(fraction.length) }
}

// Integer rational arithmetic keeps the entire database money range exact.
// Round only the final result to cents, half away from zero.
export function convertMoney(amount: string, sourceRublesPerUnit: string, targetRublesPerUnit: string) {
  const source = decimalParts(sourceRublesPerUnit)
  const target = decimalParts(targetRublesPerUnit)
  if (source.value <= 0n || target.value <= 0n) throw new Error('Exchange rates must be positive')
  const input = decimalParts(amount)
  const numerator = input.value * source.value * target.scale * 100n
  const denominator = input.scale * source.scale * target.value
  const absolute = numerator < 0n ? -numerator : numerator
  const cents = (absolute + denominator / 2n) / denominator
  return `${numerator < 0n && cents !== 0n ? '-' : ''}${cents / 100n}.${String(cents % 100n).padStart(2, '0')}`
}

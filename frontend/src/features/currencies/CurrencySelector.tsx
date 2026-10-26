import { useCurrencyDisplay, type DisplayCurrency } from './currencyDisplayContext'

export function CurrencySelector() {
  const display = useCurrencyDisplay()
  return <div className="currency-display">
    <select aria-label="Валюта отображения" aria-busy={display.pending} value={display.currency} onChange={event => display.setCurrency(event.target.value as DisplayCurrency)}>
      <option value="RUB">RUB</option><option value="USD">USD</option><option value="EUR">EUR</option>
    </select>
    {display.error && <div className="currency-error" role="alert">Не удалось переключить валюту.
      <button className="text-button" onClick={display.retry}>Повторить</button></div>}
  </div>
}

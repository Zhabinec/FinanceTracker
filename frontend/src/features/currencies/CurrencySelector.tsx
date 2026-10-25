import { formatDate } from '../../shared/formatting'
import { useCurrencyDisplay, type DisplayCurrency } from './currencyDisplayContext'

export function CurrencySelector() {
  const display = useCurrencyDisplay()
  return <section className="currency-display" aria-label="Настройки отображения валюты">
    <label>Валюта отображения<select value={display.currency} onChange={event => display.setCurrency(event.target.value as DisplayCurrency)}>
      <option value="RUB">RUB · Рубль</option><option value="USD">USD · Доллар</option><option value="EUR">EUR · Евро</option>
    </select></label>
    <p className="dashboard-note">Меняется только отображение. Ввод и редактирование — в исходной валюте.
      {display.pending && ' Получаем курс; пока показаны исходные суммы.'}
      {display.rateDate && <> Справочный курс <a href="https://www.cbr.ru/currency_base/daily/" target="_blank" rel="noreferrer">ЦБ РФ</a> на {formatDate(display.rateDate)}. Это не курс обмена банка.</>}
    </p>
    {display.error && <div className="notice error" role="alert">Курс недоступен для пересчёта. Суммы показаны в исходной валюте.
      <button className="text-button" onClick={display.retry}>Повторить</button></div>}
  </section>
}

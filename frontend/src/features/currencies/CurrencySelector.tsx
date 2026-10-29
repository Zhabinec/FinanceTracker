import { useCurrencyDisplay, type DisplayCurrency } from './currencyDisplayContext'
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'

const currencies: { code: DisplayCurrency; name: string }[] = [
  { code: 'RUB', name: 'Российский рубль' }, { code: 'USD', name: 'Доллар США' }, { code: 'EUR', name: 'Евро' },
]

export function CurrencySelector() {
  const display = useCurrencyDisplay()
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const container = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const listId = useId()
  useEffect(() => {
    if (!open) return
    const dismiss = (event: PointerEvent | FocusEvent) => {
      if (event.target instanceof Node && !container.current?.contains(event.target)) setOpen(false)
    }
    document.addEventListener('pointerdown', dismiss)
    document.addEventListener('focusin', dismiss)
    return () => { document.removeEventListener('pointerdown', dismiss); document.removeEventListener('focusin', dismiss) }
  }, [open])
  const show = () => { setActive(currencies.findIndex(item => item.code === display.currency)); setOpen(true) }
  const choose = (index: number) => { display.setCurrency(currencies[index].code); setOpen(false); trigger.current?.focus() }
  const keyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); setOpen(false); return }
    if (event.key === 'Tab') { setOpen(false); return }
    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      event.preventDefault()
      if (!open) { show(); if (event.key === 'Home') setActive(0); if (event.key === 'End') setActive(2); return }
      setActive(previous => event.key === 'Home' ? 0 : event.key === 'End' ? 2 : (previous + (event.key === 'ArrowDown' ? 1 : 2)) % 3)
    } else if ((event.key === 'Enter' || event.key === ' ') && open) { event.preventDefault(); choose(active) }
  }
  return <div className="currency-display" ref={container}>
    <button ref={trigger} type="button" role="combobox" className="currency-trigger" aria-label="Валюта отображения"
      aria-busy={display.pending} aria-expanded={open} aria-haspopup="listbox" aria-controls={listId}
      aria-activedescendant={open ? `${listId}-${active}` : undefined} onKeyDown={keyDown} onClick={() => open ? setOpen(false) : show()}>
      {display.currency}<svg viewBox="0 0 20 20" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2.25" aria-hidden="true"><path d="m5 7.5 5 5 5-5" strokeLinecap="round" strokeLinejoin="round" /></svg>
    </button>
    {open && <ul id={listId} role="listbox" aria-label="Валюты" className="currency-options">
      {currencies.map((item, index) => <li key={item.code} id={`${listId}-${index}`} role="option" aria-selected={display.currency === item.code}
        className={active === index ? 'highlighted' : ''} onPointerMove={() => setActive(index)} onMouseDown={event => event.preventDefault()} onClick={() => choose(index)}>
        <strong>{item.code}</strong><span>{item.name}</span><span className="currency-check" aria-hidden="true">{display.currency === item.code ? '✓' : ''}</span>
      </li>)}
    </ul>}
    {display.error && <div className="currency-error" role="alert">Не удалось переключить валюту.
      <button className="text-button" onClick={display.retry}>Повторить</button></div>}
  </div>
}

import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { daysInMonth, displayCalendarValue, maskCalendarInput, monthNames, parseCalendarValue, shiftMonth, type CalendarKind } from './calendar'

const dayLabel = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'long', timeZone: 'UTC' })
const currentDay = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Moscow' })

function CalendarDialog({ value, kind, label, onSelect, onDismiss }: {
  value: string; kind: CalendarKind; label: string; onSelect: (value: string) => void; onDismiss: () => void
}) {
  const today = currentDay.format(new Date())
  const initial = value || today
  const [view, setView] = useState(initial.slice(0, 7))
  const [mode, setMode] = useState<CalendarKind>(kind)
  const [focused, setFocused] = useState(kind === 'date' && value ? Number(value.slice(8)) : 1)
  const dialog = useRef<HTMLDialogElement>(null)
  const [year, month] = view.split('-').map(Number)
  useEffect(() => {
    const element = dialog.current!
    element.showModal()
    return () => element.close()
  }, [])
  useEffect(() => {
    dialog.current?.querySelector<HTMLElement>(mode === 'date' ? `[data-day="${focused}"]` : `[data-month="${view}"]`)?.focus()
  }, [view, focused, mode])
  const pick = (next: string) => { onSelect(next); dialog.current?.close() }
  const move = (delta: number) => {
    const next = shiftMonth(view, delta)
    if (next) { setView(next); setFocused(day => Math.min(day, daysInMonth(next))) }
  }
  const dayKey = (event: KeyboardEvent) => {
    const day = Number((event.target as HTMLElement).dataset.day)
    if (!day) return
    const weekday = (new Date(Date.UTC(year, month - 1, day)).getUTCDay() + 6) % 7
    const offsets: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7, Home: -weekday, End: 6 - weekday }
    if (event.key === 'PageUp' || event.key === 'PageDown') { event.preventDefault(); move(event.key === 'PageUp' ? -1 : 1); return }
    if (!(event.key in offsets)) return
    event.preventDefault()
    const date = new Date(Date.UTC(year, month - 1, day + offsets[event.key]))
    const next = date.toISOString().slice(0, 10)
    if (parseCalendarValue(next, 'date')) { setView(next.slice(0, 7)); setFocused(date.getUTCDate()) }
  }
  const monthKey = (event: KeyboardEvent) => {
    const source = (event.target as HTMLElement).dataset.month
    if (!source) return
    const number = Number(source.slice(5))
    const offsets: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -3, ArrowDown: 3,
      Home: 1 - number, End: 12 - number, PageUp: -12, PageDown: 12 }
    if (!(event.key in offsets)) return
    event.preventDefault()
    const next = shiftMonth(source, offsets[event.key])
    if (next) setView(next)
  }
  const firstWeekday = (new Date(Date.UTC(year, month - 1, 1)).getUTCDay() + 6) % 7
  const cells = Array.from({ length: Math.ceil((firstWeekday + daysInMonth(view)) / 7) * 7 }, (_, index) => {
    const day = index - firstWeekday + 1
    return day > 0 && day <= daysInMonth(view) ? day : null
  })
  return <dialog ref={dialog} className="calendar-dialog" aria-label={`Календарь: ${label}`} onClose={() => { if (!dialog.current?.open) onDismiss() }}
    onClick={event => {
      if (event.target !== event.currentTarget) return
      const bounds = event.currentTarget.getBoundingClientRect()
      if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.current?.close()
    }}>
    <div className="calendar-content">
      <div className="calendar-title"><h2>{label}</h2>
        <button type="button" className="picker-icon-button" aria-label="Закрыть календарь" onClick={() => dialog.current?.close()}>×</button></div>
      <div className="calendar-heading">
        <button type="button" className="picker-icon-button" aria-label={mode === 'date' ? 'Предыдущий месяц' : 'Предыдущий год'}
          disabled={mode === 'date' ? !shiftMonth(view, -1) : year <= 1900} onClick={() => move(mode === 'date' ? -1 : -12)}>‹</button>
        <button type="button" className="calendar-period" aria-label="Выбрать месяц и год" onClick={() => setMode('month')}>
          {mode === 'date' ? `${monthNames[month - 1]} ${year}` : year}</button>
        <button type="button" className="picker-icon-button" aria-label={mode === 'date' ? 'Следующий месяц' : 'Следующий год'}
          disabled={mode === 'date' ? !shiftMonth(view, 1) : year >= 9998} onClick={() => move(mode === 'date' ? 1 : 12)}>›</button>
      </div>
      <p className="loading-label" aria-live="polite">{monthNames[month - 1]} {year}</p>
      {mode === 'month' ? <>
        <label className="calendar-year">Год<input key={year} type="number" min="1900" max="9998" defaultValue={year}
          onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); event.currentTarget.blur() } }}
          onBlur={event => { const next = Number(event.target.value); if (Number.isInteger(next) && next >= 1900 && next <= 9998) setView(`${next}-${String(month).padStart(2, '0')}`); else event.target.value = String(year) }} /></label>
        <div className="calendar-months" role="group" aria-label="Месяцы" onKeyDown={monthKey}>{monthNames.map((name, index) => {
          const next = `${year}-${String(index + 1).padStart(2, '0')}`
          return <button type="button" key={name} data-month={next} tabIndex={next === view ? 0 : -1} aria-pressed={next === value.slice(0, 7)}
            onClick={() => { if (kind === 'month') pick(next); else { setView(next); setFocused(1); setMode('date') } }}>{name}</button>
        })}</div>
      </> : <div className="calendar-grid" role="grid" aria-label={`${monthNames[month - 1]} ${year}`} onKeyDown={dayKey}>
        <div role="row" className="calendar-week">{['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'].map(day => <span key={day} role="columnheader">{day}</span>)}</div>
        {Array.from({ length: cells.length / 7 }, (_, week) => <div role="row" className="calendar-week" key={week}>
          {cells.slice(week * 7, week * 7 + 7).map((day, index) => {
            if (day === null) return <span role="gridcell" key={`empty-${index}`} />
            const iso = `${view}-${String(day).padStart(2, '0')}`
            return <button type="button" role="gridcell" key={day} data-day={day} tabIndex={day === focused ? 0 : -1}
              aria-label={dayLabel.format(new Date(`${iso}T00:00:00Z`))}
              aria-selected={iso === value} aria-current={iso === today ? 'date' : undefined} onClick={() => pick(iso)}>{day}</button>
          })}
        </div>)}
      </div>}
      <div className="calendar-footer"><button type="button" className="ghost-button" onClick={() => pick(kind === 'date' ? today : today.slice(0, 7))}>
        {kind === 'date' ? 'Сегодня' : 'Текущий месяц'}</button>
        <button type="button" className="text-button" onClick={() => dialog.current?.close()}>Отмена</button></div>
    </div>
  </dialog>
}

export function CalendarField({ label, value, onChange, kind = 'date', disabled = false, required = false }: {
  label: string; value: string; onChange: (value: string) => void; kind?: CalendarKind; disabled?: boolean; required?: boolean
}) {
  const id = useId()
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<{ source: string; text: string } | null>(null)
  const [touched, setTouched] = useState(false)
  const invalid = draft?.source === value
  return <div className="calendar-field">
    <label htmlFor={id}>{label}</label>
    <div className="calendar-input">
      <input id={id} type="text" inputMode="numeric" autoComplete="off" disabled={disabled} required={required}
        placeholder={kind === 'date' ? 'ДД.ММ.ГГГГ' : 'ММ.ГГГГ'} maxLength={kind === 'date' ? 10 : 7}
        aria-invalid={invalid || undefined} aria-describedby={invalid && touched ? `${id}-error` : undefined}
        value={invalid ? draft!.text : displayCalendarValue(value, kind)} onBlur={() => setTouched(true)}
        onChange={event => {
          const text = maskCalendarInput(event.target.value, kind), next = parseCalendarValue(text, kind)
          setTouched(false)
          if (next !== null) { setDraft(null); onChange(next) }
          else { setDraft({ source: required ? '' : value, text }); if (required) onChange('') }
        }} />
      <button type="button" className="calendar-trigger" aria-label={`Открыть календарь: ${label}`} aria-haspopup="dialog" aria-expanded={open}
        disabled={disabled} onClick={() => setOpen(true)}>
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
          <rect x="3" y="5" width="18" height="16" rx="3" /><path d="M7 3v4m10-4v4M3 11h18m-12 5h6" /></svg>
      </button>
    </div>
    {invalid && touched && <span className="field-error" id={`${id}-error`}>Введите {kind === 'date' ? 'корректную дату' : 'месяц'} с 1900 по 9998 год.</span>}
    {open && <CalendarDialog label={label} value={value} kind={kind} onDismiss={() => setOpen(false)}
      onSelect={next => { setDraft(null); setTouched(false); onChange(next) }} />}
  </div>
}

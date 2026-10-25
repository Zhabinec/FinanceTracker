import { forwardRef, useId, useState, type InputHTMLAttributes } from 'react'

// Keep native editing and clipboard behavior; never trim or normalize a password.
export const PasswordInput = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { label: string }>(
  function PasswordInput({ label, ...props }, ref) {
    const [visible, setVisible] = useState(false)
    const generatedId = useId()
    const id = props.id ?? generatedId
    return <div className="password-field"><label htmlFor={id}>{label}</label><span className="password-input">
      <input autoCapitalize="none" autoCorrect="off" spellCheck={false} {...props} id={id} ref={ref} type={visible ? 'text' : 'password'} />
      <button type="button" className="password-toggle" aria-label={visible ? 'Скрыть пароль' : 'Показать пароль'}
        aria-pressed={visible} onClick={() => setVisible((previous) => !previous)}>
        {visible ? 'Скрыть' : 'Показать'}
      </button>
    </span></div>
  },
)

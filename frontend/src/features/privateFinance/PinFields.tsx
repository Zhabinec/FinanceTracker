import { PasswordInput } from '../../shared/PasswordInput'

export function PinFields({ pin, repeat, onPin, onRepeat }: { pin: string; repeat: string; onPin: (value: string) => void; onRepeat: (value: string) => void }) {
  return <>
    <PasswordInput label="Новый PIN" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="off" value={pin} onChange={e => onPin(e.target.value)} />
    <PasswordInput label="Повторите PIN" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="off" value={repeat} onChange={e => onRepeat(e.target.value)} />
    <p>6 цифр для этого устройства. На общем устройстве PIN лучше не сохранять. При краже файлов браузера он слабее кода восстановления.</p>
  </>
}

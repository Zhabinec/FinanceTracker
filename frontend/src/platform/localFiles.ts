export type FileSaveResult = 'requested' | 'shared' | 'cancelled'

function canShareFile(file: File) {
  try { return typeof navigator.share === 'function' && typeof navigator.canShare === 'function' && navigator.canShare({ files: [file] }) }
  catch { return false }
}
export function canSaveLocalFile(inTelegram: boolean, type = 'text/plain') {
  return !inTelegram || canShareFile(new File(['test'], 'finance.txt', { type }))
}
// Secrets stay local. Telegram.downloadFile requires an HTTPS URL, so it cannot
// safely replace a blob download of the recovery key without changing our trust model.
export async function saveLocalFile(name: string, content: string, type: string, inTelegram: boolean): Promise<FileSaveResult> {
  const file = new File([content], name, { type })
  if (inTelegram) {
    if (!canShareFile(file)) throw new Error('Эта версия Telegram не поддерживает сохранение файлов. Откройте приложение в обычном браузере, чтобы скачать копию.')
    try { await navigator.share({ files: [file] }); return 'shared' }
    catch (error) {
      if (error instanceof Error && error.name === 'AbortError') return 'cancelled'
      throw new Error('Не удалось сохранить файл. Попробуйте в обычном браузере.', { cause: error })
    }
  }
  const url = URL.createObjectURL(file), link = document.createElement('a')
  link.href = url; link.download = name; link.style.display = 'none'
  document.body.append(link)
  try { link.click() } finally {
    link.remove()
    // WebViews may consume a blob asynchronously; do not revoke it after just 1s.
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  }
  return 'requested'
}

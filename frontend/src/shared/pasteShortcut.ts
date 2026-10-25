export function preventRepeatedPaste(event: KeyboardEvent) {
  if (!event.repeat || !(event.ctrlKey || event.metaKey) || event.altKey ||
      (event.code !== 'KeyV' && event.key.toLowerCase() !== 'v')) return
  const target = event.target
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement ||
      (target instanceof HTMLElement && target.isContentEditable)) event.preventDefault()
}

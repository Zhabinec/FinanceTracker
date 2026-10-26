export function csvHeaders(content: string, delimiter: string): string[] {
  const fields: string[] = []
  let field = '', quoted = false
  const text = content.replace(/^\uFEFF/, '')
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (c === '"') {
      if (quoted && text[i + 1] === '"') { field += '"'; i++ }
      else quoted = !quoted
    } else if (!quoted && c === delimiter) { fields.push(field); field = '' }
    else if (!quoted && (c === '\r' || c === '\n')) break
    else field += c
  }
  fields.push(field)
  return fields.slice(0, 50)
}

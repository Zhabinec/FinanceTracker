import { Component, type ReactNode } from 'react'

export class PageBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  render() {
    if (this.state.failed) return <div className="notice error" role="alert">
      <p>Не удалось открыть раздел. Проверьте соединение и обновите страницу.</p>
      <button className="ghost-button" onClick={() => window.location.reload()}>Обновить страницу</button>
    </div>
    return this.props.children
  }
}

import { Link } from 'react-router-dom'
import type { ReactNode } from 'react'

export function AuthLayout({ title, subtitle, alternateText, alternateLink,
  alternateLabel, children }: { title: string; subtitle: string; alternateText: string;
    alternateLink: string; alternateLabel: string; children: ReactNode }) {
  return (
    <main className="auth-shell">
      <section className="brand-panel">
        <div className="brand-mark">₽</div>
        <div>
          <p className="eyebrow">Finance Tracker</p>
          <h1>Деньги любят<br />ясность.</h1>
          <p className="brand-copy">Доходы, расходы и категории — в одном спокойном пространстве без лишнего шума.</p>
        </div>
        <p className="brand-footnote">Личный финансовый трекер</p>
      </section>
      <section className="form-panel">
        <div className="form-card">
          <p className="eyebrow">Личный кабинет</p>
          <h2>{title}</h2>
          <p className="form-subtitle">{subtitle}</p>
          {children}
          <p className="alternate">{alternateText} <Link to={alternateLink}>{alternateLabel}</Link></p>
        </div>
      </section>
    </main>
  )
}

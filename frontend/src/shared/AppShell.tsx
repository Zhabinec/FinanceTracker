import { Suspense, useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { telegramBackButton } from '../platform/telegram'
import { useAuth } from '../features/auth/useAuth'
import { PageBoundary } from './PageBoundary'
import { CurrencySelector } from '../features/currencies/CurrencySelector'
import { DataSkeleton } from './DataSkeleton'

const sections = [
  { path: '/', label: 'Обзор', title: 'Обзор финансов', hint: 'Главное о ваших доходах и расходах.', icon: 'M3 10 12 3l9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z' },
  { path: '/categories', label: 'Категории', title: 'Категории', hint: 'Нажмите на плашку, чтобы быстро добавить операцию.', icon: 'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z' },
  { path: '/transactions', label: 'История', title: 'История', hint: '', icon: 'M4 7h16m-4-4 4 4-4 4M20 17H4m4-4-4 4 4 4' },
  { path: '/accounts', label: 'Счета', title: 'Счета', hint: 'Остатки на картах, наличные и накопления.', icon: 'M3 5h17v15H3zM3 5V3h14v2m-2 7h6v5h-6z' },
  { path: '/account', label: 'Аккаунт', title: 'Аккаунт', hint: 'Ваш профиль и доступ к приложению.', icon: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM4 21v-2a8 8 0 0 1 16 0v2' },
]

function Icon({ path }: { path: string }) {
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"
    strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={path} /></svg>
}

export function AppShell() {
  const { signOut } = useAuth()
  const [leaving, setLeaving] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  useEffect(() => {
    const button = telegramBackButton()
    if (!button) return
    const back = () => navigate('/')
    if (location.pathname === '/') button.hide()
    else { button.show(); button.onClick(back) }
    return () => { button.offClick(back); button.hide() }
  }, [location.pathname, navigate])
  const section = sections.find(item => item.path === location.pathname) ?? sections[0]
  useEffect(() => {
    document.title = `${section.title} · Finance Tracker`
    window.scrollTo({ top: 0, behavior: 'instant' })
  }, [section.title])
  return <div className="app-shell">
    <a className="skip-link" href="#app-main">К содержимому</a>
    <header className="app-chrome">
      <div className="app-chrome-inner">
        <Link to="/" className="app-brand" aria-label="Finance Tracker — главная">
          <span className="app-brand-mark"><Icon path="M3 5h17v15H3zM15 11h6v5h-6z" /></span>
          <span>Finance Tracker<small>Личные финансы</small></span>
        </Link>
        <nav className="app-navigation" aria-label="Основная навигация">
          {sections.map(item => <NavLink key={item.path} to={item.path} end={item.path === '/'}
            className={({ isActive }) => `app-nav-link${isActive ? ' active' : ''}`}>
            <Icon path={item.icon} /><span>{item.label}</span>
          </NavLink>)}
        </nav>
        <button className="app-logout" type="button" aria-label="Выйти" title="Выйти" aria-busy={leaving}
          disabled={leaving} onClick={async () => { setLeaving(true); await signOut() }}>
          <Icon path="M9 4H4v16h5M14 8l4 4-4 4M8 12h10" />
        </button>
      </div>
    </header>
    <main className="app-content" id="app-main" tabIndex={-1}>
      <div className="page-heading"><div className="page-heading-copy"><h1>{section.title}</h1>
        {section.path === '/categories' && <p>{section.hint}</p>}</div><CurrencySelector /></div>
      <PageBoundary key={location.pathname}><Suspense fallback={<DataSkeleton label="Загружаем раздел…" kind={section.path === '/' ? 'overview' : section.path === '/accounts' ? 'cards' : 'rows'} />}><Outlet /></Suspense></PageBoundary>
    </main>
  </div>
}

import { useLocation } from 'react-router-dom'
import { Sun, Moon, LogOut } from 'lucide-react'
import { useAuth } from '../hooks/useAuth'
import { useTheme } from '../hooks/useTheme'

// Map the current route to a page title shown on the left of the header.
function usePageTitle(): string {
  const { pathname } = useLocation()
  if (pathname === '/') return 'Dashboard'
  if (pathname.startsWith('/profiles')) return 'Profiles'
  if (pathname.startsWith('/history')) return 'Run History'
  if (pathname.startsWith('/results')) return 'Comparison Result'
  if (pathname.startsWith('/databases')) return 'Databases'
  if (pathname.startsWith('/admin')) return 'Admin'
  return 'db-tula'
}

export default function Header() {
  const title = usePageTitle()
  const { user, logout } = useAuth()
  const { theme, toggle } = useTheme()

  return (
    <header className="sticky top-0 z-30 flex h-16 items-center justify-between border-b border-slate-200 bg-white/80 px-8 backdrop-blur-sm dark:border-border-soft dark:bg-bg-main/80">
      <h1 className="text-lg font-semibold text-slate-900 dark:text-text-primary">{title}</h1>

      <div className="flex items-center gap-2">
        <button
          onClick={toggle}
          title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
          className="flex h-9 w-9 items-center justify-center rounded-lg text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900 dark:text-text-muted dark:hover:bg-bg-elevated dark:hover:text-text-primary"
        >
          {theme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
        </button>

        <div className="ml-1 flex items-center gap-2.5 rounded-lg py-1 pl-1 pr-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-brand-orange text-xs font-semibold text-white">
            {user?.name?.charAt(0).toUpperCase() ?? '?'}
          </div>
          <div className="hidden min-w-0 sm:block">
            <p className="truncate text-xs font-medium text-slate-900 dark:text-text-primary">{user?.name}</p>
            <p className="truncate text-xs text-slate-500 dark:text-text-muted">{user?.role}</p>
          </div>
          <button
            onClick={logout}
            title="Sign out"
            className="ml-1 flex h-9 w-9 items-center justify-center rounded-lg text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900 dark:text-text-muted dark:hover:bg-bg-elevated dark:hover:text-text-primary"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </header>
  )
}

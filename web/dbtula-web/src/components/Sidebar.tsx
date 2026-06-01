import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { LayoutDashboard, GitCompare, History, Database, ShieldCheck, LogOut, ChevronRight, ChevronLeft, Sun, Moon } from 'lucide-react'
import { useAuth } from '../hooks/useAuth'
import { useTheme } from '../hooks/useTheme'
import { cn } from '../lib/utils'

const nav = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/profiles', label: 'Profiles', icon: GitCompare },
  { to: '/history', label: 'Run History', icon: History },
]

const adminNav = [
  { to: '/databases', label: 'Databases', icon: Database },
  { to: '/admin', label: 'Admin', icon: ShieldCheck },
]

export default function Sidebar() {
  const { user, logout } = useAuth()
  const { theme, toggle } = useTheme()
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem('dbtula-sidebar') === 'collapsed')

  const setCollapsedState = (v: boolean) => {
    setCollapsed(v)
    localStorage.setItem('dbtula-sidebar', v ? 'collapsed' : 'expanded')
  }

  const NavItem = ({ to, label, icon: Icon }: { to: string; label: string; icon: React.ElementType }) => (
    <div className="relative group">
      <NavLink
        to={to}
        end={to === '/'}
        className={({ isActive }) =>
          cn(
            'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
            collapsed ? 'justify-center px-2' : '',
            isActive
              ? 'bg-slate-700 text-white'
              : 'text-slate-400 hover:bg-slate-800 hover:text-white'
          )
        }
      >
        <Icon className="h-4 w-4 shrink-0" />
        {!collapsed && label}
      </NavLink>
      {/* Tooltip when collapsed */}
      {collapsed && (
        <div className="absolute left-full ml-2 top-1/2 -translate-y-1/2 bg-slate-700 text-white text-xs px-2 py-1 rounded whitespace-nowrap opacity-0 group-hover:opacity-100 pointer-events-none z-50 transition-opacity">
          {label}
        </div>
      )}
    </div>
  )

  return (
    <div className={cn(
      'fixed inset-y-0 left-0 flex flex-col bg-slate-900 border-r border-slate-800 transition-all duration-200',
      collapsed ? 'w-16' : 'w-60'
    )}>
      {/* Logo */}
      <div className={cn('flex h-16 items-center border-b border-slate-800', collapsed ? 'justify-center px-2' : 'gap-3 px-5')}>
        <img src="/logo.svg" alt="db-tula" className="h-8 w-8 rounded-lg shrink-0" />
        {!collapsed && <span className="font-semibold text-white text-sm tracking-wide">db-tula</span>}
        <button
          onClick={() => setCollapsedState(!collapsed)}
          className={cn('text-slate-500 hover:text-white transition-colors', collapsed ? '' : 'ml-auto')}
          title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {collapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronLeft className="h-4 w-4" />}
        </button>
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto px-2 py-4 space-y-1">
        {nav.map(item => <NavItem key={item.to} {...item} />)}

        {user?.role === 'Admin' && (
          <>
            {!collapsed && (
              <div className="px-3 pt-4 pb-1">
                <p className="text-xs font-semibold uppercase tracking-wider text-slate-600">Admin</p>
              </div>
            )}
            {collapsed && <div className="border-t border-slate-800 my-2" />}
            {adminNav.map(item => <NavItem key={item.to} {...item} />)}
          </>
        )}
      </nav>

      {/* User footer */}
      <div className="border-t border-slate-800 p-2">
        {/* Dark mode toggle */}
        <button
          onClick={toggle}
          className={cn(
            'w-full flex items-center gap-3 rounded-lg px-3 py-2 text-slate-400 hover:text-white hover:bg-slate-800 transition-colors text-sm mb-1',
            collapsed ? 'justify-center' : ''
          )}
          title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        >
          {theme === 'dark' ? <Sun className="h-4 w-4 shrink-0" /> : <Moon className="h-4 w-4 shrink-0" />}
          {!collapsed && (theme === 'dark' ? 'Light mode' : 'Dark mode')}
        </button>

        {/* User info + logout */}
        <div className={cn('flex items-center gap-3 rounded-lg px-2 py-2', collapsed ? 'justify-center' : '')}>
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-600 text-white text-xs font-semibold shrink-0">
            {user?.name?.charAt(0).toUpperCase() ?? '?'}
          </div>
          {!collapsed && (
            <div className="flex-1 min-w-0">
              <p className="text-xs font-medium text-white truncate">{user?.name}</p>
              <p className="text-xs text-slate-500 truncate">{user?.role}</p>
            </div>
          )}
          <button onClick={logout} className="text-slate-500 hover:text-white transition-colors" title="Sign out">
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  )
}

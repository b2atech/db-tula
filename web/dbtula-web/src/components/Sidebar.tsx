import { NavLink } from 'react-router-dom'
import { LayoutDashboard, GitCompare, History, Database, ShieldCheck, LogOut, ChevronRight } from 'lucide-react'
import { useAuth } from '../hooks/useAuth'
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

  const NavItem = ({ to, label, icon: Icon }: { to: string; label: string; icon: React.ElementType }) => (
    <NavLink
      to={to}
      end={to === '/'}
      className={({ isActive }) =>
        cn(
          'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
          isActive
            ? 'bg-slate-700 text-white'
            : 'text-slate-400 hover:bg-slate-800 hover:text-white'
        )
      }
    >
      <Icon className="h-4 w-4 shrink-0" />
      {label}
    </NavLink>
  )

  return (
    <div className="fixed inset-y-0 left-0 flex w-60 flex-col bg-slate-900 border-r border-slate-800">
      {/* Logo */}
      <div className="flex h-16 items-center gap-3 px-6 border-b border-slate-800">
        <div className="flex h-7 w-7 items-center justify-center rounded-md bg-indigo-600 text-white text-xs font-bold">dt</div>
        <span className="font-semibold text-white text-sm">db-tula</span>
        <ChevronRight className="h-3 w-3 text-slate-600 ml-auto" />
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto px-3 py-4 space-y-1">
        {nav.map(item => <NavItem key={item.to} {...item} />)}

        {user?.role === 'Admin' && (
          <>
            <div className="px-3 pt-4 pb-1">
              <p className="text-xs font-semibold uppercase tracking-wider text-slate-600">Admin</p>
            </div>
            {adminNav.map(item => <NavItem key={item.to} {...item} />)}
          </>
        )}
      </nav>

      {/* User footer */}
      <div className="border-t border-slate-800 p-3">
        <div className="flex items-center gap-3 rounded-lg px-2 py-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-600 text-white text-xs font-semibold shrink-0">
            {user?.name?.charAt(0).toUpperCase() ?? '?'}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-xs font-medium text-white truncate">{user?.name}</p>
            <p className="text-xs text-slate-500 truncate">{user?.role}</p>
          </div>
          <button
            onClick={logout}
            className="text-slate-500 hover:text-white transition-colors"
            title="Sign out"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  )
}

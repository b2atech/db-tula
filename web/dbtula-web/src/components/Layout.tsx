import Sidebar from './Sidebar'
import Header from './Header'

export default function Layout({ children }: { children: React.ReactNode }) {
  const collapsed = localStorage.getItem('dbtula-sidebar') === 'collapsed'

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-bg-main">
      <Sidebar />
      <div
        className="transition-all duration-200"
        style={{ marginLeft: collapsed ? '4rem' : '15rem' }}
      >
        <Header />
        <main className="min-h-[calc(100vh-4rem)] p-8">{children}</main>
      </div>
    </div>
  )
}

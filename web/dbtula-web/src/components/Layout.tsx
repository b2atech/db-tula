import Sidebar from './Sidebar'

export default function Layout({ children }: { children: React.ReactNode }) {
  const collapsed = localStorage.getItem('dbtula-sidebar') === 'collapsed'

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950">
      <Sidebar />
      <main
        className="min-h-screen p-8 transition-all duration-200"
        style={{ marginLeft: collapsed ? '4rem' : '15rem' }}
      >
        {children}
      </main>
    </div>
  )
}

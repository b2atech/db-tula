import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAuth } from './hooks/useAuth'
import { ErrorBoundary } from './components/ErrorBoundary'
import { ToastContainer } from './components/Toast'
import Layout from './components/Layout'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Profiles from './pages/Profiles'
import History from './pages/History'
import ComparisonResult from './pages/ComparisonResult'
import Databases from './pages/Databases'
import Admin from './pages/Admin'

const qc = new QueryClient({ defaultOptions: { queries: { retry: 1 } } })

function AuthGuard({ children, adminOnly = false }: { children: React.ReactNode; adminOnly?: boolean }) {
  const { user, isLoading, isAuthenticated } = useAuth()
  if (isLoading) return (
    <div className="min-h-screen bg-slate-900 flex items-center justify-center">
      <div className="text-slate-400 text-sm">Loading...</div>
    </div>
  )
  if (!isAuthenticated) return <Navigate to="/login" replace />
  if (adminOnly && user?.role !== 'Admin') return <Navigate to="/" replace />
  return <Layout>{children}</Layout>
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<AuthGuard><Dashboard /></AuthGuard>} />
      <Route path="/profiles" element={<AuthGuard><Profiles /></AuthGuard>} />
      <Route path="/history" element={<AuthGuard><History /></AuthGuard>} />
      <Route path="/results/:id" element={<AuthGuard><ComparisonResult /></AuthGuard>} />
      <Route path="/databases" element={<AuthGuard adminOnly><Databases /></AuthGuard>} />
      <Route path="/admin" element={<AuthGuard adminOnly><Admin /></AuthGuard>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default function App() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={qc}>
        <BrowserRouter>
          <AppRoutes />
          <ToastContainer />
        </BrowserRouter>
      </QueryClientProvider>
    </ErrorBoundary>
  )
}

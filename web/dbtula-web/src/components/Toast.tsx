import { useEffect, useState } from 'react'
import { X } from 'lucide-react'

interface ToastMessage { id: number; text: string; type: 'error' | 'info' | 'success' }

let toastId = 0
export function showToast(text: string, type: ToastMessage['type'] = 'error') {
  window.dispatchEvent(new CustomEvent('dbtula:toast', { detail: { id: ++toastId, text, type } }))
}

export function ToastContainer() {
  const [toasts, setToasts] = useState<ToastMessage[]>([])

  useEffect(() => {
    const handler = (e: Event) => {
      const msg = (e as CustomEvent).detail as ToastMessage
      setToasts(prev => [...prev, msg])
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== msg.id)), 5000)
    }
    window.addEventListener('dbtula:toast', handler)
    return () => window.removeEventListener('dbtula:toast', handler)
  }, [])

  if (!toasts.length) return null

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map(t => (
        <div key={t.id} className={`flex items-center gap-3 px-4 py-3 rounded-lg shadow-lg text-sm font-medium text-white max-w-sm
          ${t.type === 'error' ? 'bg-red-600' : t.type === 'success' ? 'bg-green-600' : 'bg-slate-700'}`}>
          <span className="flex-1">{t.text}</span>
          <button onClick={() => setToasts(prev => prev.filter(x => x.id !== t.id))}>
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  )
}

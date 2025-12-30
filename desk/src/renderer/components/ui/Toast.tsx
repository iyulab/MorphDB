import { useEffect, useState, type ReactElement } from 'react'
import { X, CheckCircle, AlertCircle, AlertTriangle, Info } from 'lucide-react'
import { useToastStore, type Toast as ToastType, type ToastType as ToastVariant } from '@/stores/toastStore'
import { cn } from '@/lib/utils'
import './Toast.css'

const icons: Record<ToastVariant, typeof CheckCircle> = {
  success: CheckCircle,
  error: AlertCircle,
  warning: AlertTriangle,
  info: Info
}

const styles: Record<ToastVariant, string> = {
  success: 'toast-success',
  error: 'toast-error',
  warning: 'toast-warning',
  info: 'toast-info'
}

interface ToastItemProps {
  toast: ToastType
  onRemove: (id: string) => void
}

function ToastItem({ toast, onRemove }: ToastItemProps): ReactElement {
  const [isExiting, setIsExiting] = useState(false)
  const Icon = icons[toast.type]

  const handleRemove = (): void => {
    setIsExiting(true)
    setTimeout(() => onRemove(toast.id), 200)
  }

  // Auto-trigger exit animation slightly before removal
  useEffect(() => {
    if (toast.duration && toast.duration > 0) {
      const timer = setTimeout(() => {
        setIsExiting(true)
      }, toast.duration - 200)
      return () => clearTimeout(timer)
    }
  }, [toast.duration])

  return (
    <div
      className={cn(
        'toast-item',
        styles[toast.type],
        isExiting && 'toast-exit'
      )}
      role="alert"
    >
      <Icon className="toast-icon" />
      <div className="toast-content">
        <div className="toast-title">{toast.title}</div>
        {toast.message && <div className="toast-message">{toast.message}</div>}
      </div>
      <button
        onClick={handleRemove}
        className="toast-close"
        aria-label="Close"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  )
}

export function ToastContainer(): ReactElement {
  const { toasts, removeToast } = useToastStore()

  return (
    <div className="toast-container" aria-live="polite">
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onRemove={removeToast} />
      ))}
    </div>
  )
}

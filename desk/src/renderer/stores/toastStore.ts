import { create } from 'zustand'

export type ToastType = 'success' | 'error' | 'warning' | 'info'

export interface Toast {
  id: string
  type: ToastType
  title: string
  message?: string
  duration?: number
}

interface ToastState {
  toasts: Toast[]

  // Actions
  addToast: (toast: Omit<Toast, 'id'>) => void
  removeToast: (id: string) => void
  clearToasts: () => void

  // Convenience methods
  success: (title: string, message?: string) => void
  error: (title: string, message?: string) => void
  warning: (title: string, message?: string) => void
  info: (title: string, message?: string) => void
}

let toastId = 0

const generateId = (): string => {
  return `toast-${++toastId}-${Date.now()}`
}

export const useToastStore = create<ToastState>()((set, get) => ({
  toasts: [],

  addToast: (toast) => {
    const id = generateId()
    const newToast: Toast = {
      id,
      duration: toast.duration ?? 5000,
      ...toast
    }

    set((state) => ({
      toasts: [...state.toasts, newToast]
    }))

    // Auto-remove after duration
    if (newToast.duration && newToast.duration > 0) {
      setTimeout(() => {
        get().removeToast(id)
      }, newToast.duration)
    }
  },

  removeToast: (id) => {
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id)
    }))
  },

  clearToasts: () => {
    set({ toasts: [] })
  },

  success: (title, message) => {
    get().addToast({ type: 'success', title, message })
  },

  error: (title, message) => {
    get().addToast({ type: 'error', title, message, duration: 8000 })
  },

  warning: (title, message) => {
    get().addToast({ type: 'warning', title, message })
  },

  info: (title, message) => {
    get().addToast({ type: 'info', title, message })
  }
}))

// Export a hook for easy access
export function useToast(): Pick<ToastState, 'success' | 'error' | 'warning' | 'info'> {
  const { success, error, warning, info } = useToastStore()
  return { success, error, warning, info }
}

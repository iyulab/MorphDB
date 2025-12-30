import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { useToastStore } from './toastStore'

describe('toastStore', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    // Reset store state
    useToastStore.setState({ toasts: [] })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('addToast', () => {
    it('should add a toast to the list', () => {
      const { addToast } = useToastStore.getState()
      addToast({ type: 'success', title: 'Test Toast' })

      const { toasts } = useToastStore.getState()
      expect(toasts).toHaveLength(1)
      expect(toasts[0].title).toBe('Test Toast')
      expect(toasts[0].type).toBe('success')
    })

    it('should generate unique IDs for each toast', () => {
      const { addToast } = useToastStore.getState()
      addToast({ type: 'info', title: 'Toast 1' })
      addToast({ type: 'info', title: 'Toast 2' })

      const { toasts } = useToastStore.getState()
      expect(toasts[0].id).not.toBe(toasts[1].id)
    })

    it('should use default duration of 5000ms', () => {
      const { addToast } = useToastStore.getState()
      addToast({ type: 'success', title: 'Test' })

      const { toasts } = useToastStore.getState()
      expect(toasts[0].duration).toBe(5000)
    })

    it('should auto-remove toast after duration', () => {
      const { addToast } = useToastStore.getState()
      addToast({ type: 'success', title: 'Test', duration: 1000 })

      expect(useToastStore.getState().toasts).toHaveLength(1)

      vi.advanceTimersByTime(1000)

      expect(useToastStore.getState().toasts).toHaveLength(0)
    })
  })

  describe('removeToast', () => {
    it('should remove a specific toast by ID', () => {
      const { addToast, removeToast } = useToastStore.getState()
      addToast({ type: 'info', title: 'Toast 1' })
      addToast({ type: 'info', title: 'Toast 2' })

      const { toasts } = useToastStore.getState()
      const idToRemove = toasts[0].id

      removeToast(idToRemove)

      const updatedToasts = useToastStore.getState().toasts
      expect(updatedToasts).toHaveLength(1)
      expect(updatedToasts[0].title).toBe('Toast 2')
    })
  })

  describe('clearToasts', () => {
    it('should clear all toasts', () => {
      const { addToast, clearToasts } = useToastStore.getState()
      addToast({ type: 'info', title: 'Toast 1' })
      addToast({ type: 'info', title: 'Toast 2' })
      addToast({ type: 'info', title: 'Toast 3' })

      expect(useToastStore.getState().toasts).toHaveLength(3)

      clearToasts()

      expect(useToastStore.getState().toasts).toHaveLength(0)
    })
  })

  describe('convenience methods', () => {
    it('should add success toast', () => {
      const { success } = useToastStore.getState()
      success('Operation successful', 'Details here')

      const { toasts } = useToastStore.getState()
      expect(toasts[0].type).toBe('success')
      expect(toasts[0].title).toBe('Operation successful')
      expect(toasts[0].message).toBe('Details here')
    })

    it('should add error toast with longer duration', () => {
      const { error } = useToastStore.getState()
      error('Something went wrong')

      const { toasts } = useToastStore.getState()
      expect(toasts[0].type).toBe('error')
      expect(toasts[0].duration).toBe(8000)
    })

    it('should add warning toast', () => {
      const { warning } = useToastStore.getState()
      warning('Be careful')

      const { toasts } = useToastStore.getState()
      expect(toasts[0].type).toBe('warning')
    })

    it('should add info toast', () => {
      const { info } = useToastStore.getState()
      info('FYI')

      const { toasts } = useToastStore.getState()
      expect(toasts[0].type).toBe('info')
    })
  })
})

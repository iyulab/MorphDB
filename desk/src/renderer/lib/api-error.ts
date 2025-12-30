import { useToastStore } from '@/stores/toastStore'

export interface ApiError {
  status: number
  message: string
  details?: string
}

/**
 * Parse an error into a standard format
 */
export function parseError(error: unknown): ApiError {
  if (error instanceof Error) {
    // Check if it's a fetch error with response
    if ('response' in error && typeof (error as { response: unknown }).response === 'object') {
      const response = (error as { response: { status?: number; data?: { message?: string } } }).response
      return {
        status: response?.status ?? 500,
        message: response?.data?.message ?? error.message,
        details: error.message
      }
    }

    return {
      status: 500,
      message: error.message,
      details: error.stack
    }
  }

  if (typeof error === 'string') {
    return {
      status: 500,
      message: error
    }
  }

  return {
    status: 500,
    message: 'An unexpected error occurred'
  }
}

/**
 * Handle API error and show toast
 */
export function handleApiError(error: unknown, customMessage?: string): void {
  const parsed = parseError(error)
  const toastStore = useToastStore.getState()

  const title = customMessage ?? getErrorTitle(parsed.status)
  const message = parsed.message

  toastStore.error(title, message)

  // Log error in development
  if (process.env.NODE_ENV === 'development') {
    console.error('[API Error]', parsed)
  }
}

function getErrorTitle(status: number): string {
  switch (status) {
    case 400:
      return 'Bad Request'
    case 401:
      return 'Authentication Required'
    case 403:
      return 'Access Denied'
    case 404:
      return 'Not Found'
    case 409:
      return 'Conflict'
    case 422:
      return 'Validation Error'
    case 429:
      return 'Too Many Requests'
    case 500:
      return 'Server Error'
    case 502:
      return 'Bad Gateway'
    case 503:
      return 'Service Unavailable'
    default:
      return 'Error'
  }
}

/**
 * Hook for API error handling in React components
 */
export function useApiErrorHandler(): {
  handleError: (error: unknown, customMessage?: string) => void
} {
  return {
    handleError: handleApiError
  }
}

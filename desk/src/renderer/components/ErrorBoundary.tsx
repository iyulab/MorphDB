import { Component, type ReactNode, type ReactElement, type ErrorInfo } from 'react'
import { useRouteError, isRouteErrorResponse, Link, useNavigate } from 'react-router-dom'
import { AlertTriangle, Home, RefreshCw, ChevronDown, ChevronUp, Bug, Copy, Check } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useState, useCallback } from 'react'

interface ErrorDetails {
  status?: string
  message: string
  stack?: string
  componentStack?: string
}

function parseError(error: unknown): ErrorDetails {
  if (isRouteErrorResponse(error)) {
    return {
      status: `${error.status}`,
      message: error.statusText || error.data?.message || 'An unexpected error occurred'
    }
  }
  if (error instanceof Error) {
    return {
      message: error.message,
      stack: error.stack
    }
  }
  return {
    message: String(error) || 'An unexpected error occurred'
  }
}

interface ErrorDisplayProps {
  error: ErrorDetails
  resetError?: () => void
  showDetails?: boolean
}

function ErrorDisplay({ error, resetError, showDetails = false }: ErrorDisplayProps): ReactElement {
  const [expanded, setExpanded] = useState(false)
  const [copied, setCopied] = useState(false)
  const navigate = useNavigate()
  const isDev = import.meta.env.DEV

  const handleReload = useCallback((): void => {
    window.location.reload()
  }, [])

  const handleGoBack = useCallback((): void => {
    navigate(-1)
  }, [navigate])

  const handleCopyError = useCallback(async (): Promise<void> => {
    const errorText = [
      `Error: ${error.message}`,
      error.status ? `Status: ${error.status}` : '',
      error.stack ? `\nStack:\n${error.stack}` : '',
      error.componentStack ? `\nComponent Stack:\n${error.componentStack}` : ''
    ].filter(Boolean).join('\n')

    await navigator.clipboard.writeText(errorText)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }, [error])

  return (
    <div className="flex h-full w-full items-center justify-center bg-background p-8">
      <div className="text-center max-w-lg w-full">
        <AlertTriangle className="mx-auto h-16 w-16 text-destructive" />

        {error.status && (
          <h1 className="mt-4 text-4xl font-bold text-foreground">{error.status}</h1>
        )}

        <h2 className="mt-2 text-xl font-semibold text-foreground">
          Something went wrong
        </h2>

        <p className="mt-2 text-muted-foreground break-words">
          {error.message}
        </p>

        {/* Action Buttons */}
        <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
          {resetError && (
            <Button variant="default" onClick={resetError}>
              <RefreshCw className="h-4 w-4 mr-2" />
              Try Again
            </Button>
          )}
          <Button variant="outline" onClick={handleReload}>
            <RefreshCw className="h-4 w-4 mr-2" />
            Reload Page
          </Button>
          <Button variant="outline" onClick={handleGoBack}>
            Go Back
          </Button>
          <Link to="/">
            <Button variant="ghost">
              <Home className="h-4 w-4 mr-2" />
              Go Home
            </Button>
          </Link>
        </div>

        {/* Development Mode: Error Details */}
        {(isDev || showDetails) && (error.stack || error.componentStack) && (
          <div className="mt-6">
            <button
              onClick={() => setExpanded(!expanded)}
              className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
            >
              <Bug className="h-4 w-4" />
              {expanded ? 'Hide' : 'Show'} Error Details
              {expanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
            </button>

            {expanded && (
              <div className="mt-3 text-left">
                <div className="flex items-center justify-between mb-2">
                  <span className="text-xs font-medium text-muted-foreground uppercase">
                    Stack Trace
                  </span>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-7 px-2"
                    onClick={handleCopyError}
                  >
                    {copied ? (
                      <Check className="h-3 w-3 mr-1" />
                    ) : (
                      <Copy className="h-3 w-3 mr-1" />
                    )}
                    {copied ? 'Copied!' : 'Copy'}
                  </Button>
                </div>

                <pre className="p-3 bg-muted rounded-md text-xs overflow-auto max-h-60 text-left whitespace-pre-wrap break-words">
                  {error.stack}
                  {error.componentStack && (
                    <>
                      {'\n\nComponent Stack:\n'}
                      {error.componentStack}
                    </>
                  )}
                </pre>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * Route-level error boundary for React Router
 * Use this as errorElement in route configuration
 */
export function RouteErrorBoundary(): ReactElement {
  const routeError = useRouteError()
  const error = parseError(routeError)

  return <ErrorDisplay error={error} />
}

// Re-export with original name for backwards compatibility
export const ErrorBoundary = RouteErrorBoundary

interface ClassErrorBoundaryProps {
  children: ReactNode
  fallback?: ReactNode
  onError?: (error: Error, errorInfo: ErrorInfo) => void
}

interface ClassErrorBoundaryState {
  hasError: boolean
  error: ErrorDetails | null
}

/**
 * Class-based error boundary for wrapping components
 * Use this to catch errors in specific parts of the component tree
 */
export class ComponentErrorBoundary extends Component<ClassErrorBoundaryProps, ClassErrorBoundaryState> {
  constructor(props: ClassErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ClassErrorBoundaryState {
    return {
      hasError: true,
      error: {
        message: error.message,
        stack: error.stack
      }
    }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // Log error for debugging
    console.error('ComponentErrorBoundary caught an error:', error, errorInfo)

    // Update state with component stack
    this.setState(prev => ({
      ...prev,
      error: prev.error ? {
        ...prev.error,
        componentStack: errorInfo.componentStack || undefined
      } : null
    }))

    // Call optional error handler
    this.props.onError?.(error, errorInfo)
  }

  handleReset = (): void => {
    this.setState({ hasError: false, error: null })
  }

  render(): ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback
      }

      return this.state.error ? (
        <ErrorDisplay error={this.state.error} resetError={this.handleReset} />
      ) : null
    }

    return this.props.children
  }
}

/**
 * Compact inline error display for smaller components
 */
interface InlineErrorProps {
  message: string
  onRetry?: () => void
}

export function InlineError({ message, onRetry }: InlineErrorProps): ReactElement {
  return (
    <div className="flex items-center gap-3 p-4 rounded-md bg-destructive/10 text-destructive">
      <AlertTriangle className="h-5 w-5 flex-shrink-0" />
      <span className="text-sm flex-1">{message}</span>
      {onRetry && (
        <Button variant="ghost" size="sm" onClick={onRetry} className="text-destructive hover:text-destructive">
          <RefreshCw className="h-4 w-4 mr-1" />
          Retry
        </Button>
      )}
    </div>
  )
}

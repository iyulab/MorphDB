import { type ReactElement } from 'react'
import { useRouteError, isRouteErrorResponse, Link } from 'react-router-dom'
import { AlertTriangle, Home, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'

export function ErrorBoundary(): ReactElement {
  const error = useRouteError()

  let errorMessage = 'An unexpected error occurred'
  let errorStatus = ''

  if (isRouteErrorResponse(error)) {
    errorStatus = `${error.status}`
    errorMessage = error.statusText || error.data?.message || errorMessage
  } else if (error instanceof Error) {
    errorMessage = error.message
  }

  const handleReload = (): void => {
    window.location.reload()
  }

  return (
    <div className="flex h-screen w-screen items-center justify-center bg-background p-8">
      <div className="text-center max-w-md">
        <AlertTriangle className="mx-auto h-16 w-16 text-destructive" />
        {errorStatus && (
          <h1 className="mt-4 text-4xl font-bold text-foreground">{errorStatus}</h1>
        )}
        <h2 className="mt-2 text-xl font-semibold text-foreground">Something went wrong</h2>
        <p className="mt-2 text-muted-foreground">{errorMessage}</p>

        <div className="mt-6 flex items-center justify-center gap-4">
          <Button variant="outline" onClick={handleReload}>
            <RefreshCw className="h-4 w-4 mr-2" />
            Reload
          </Button>
          <Link
            to="/"
            className="inline-flex items-center justify-center rounded-md text-sm font-medium transition-colors bg-primary text-primary-foreground hover:bg-primary/90 h-10 px-4 py-2"
          >
            <Home className="h-4 w-4 mr-2" />
            Go Home
          </Link>
        </div>
      </div>
    </div>
  )
}

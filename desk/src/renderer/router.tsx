import { createHashRouter, Navigate } from 'react-router-dom'
import { RootLayout } from './layouts/RootLayout'
import { ExplorerPage } from './routes/explorer'
import { SettingsPage } from './routes/settings'
import { ProjectsPage } from './routes/projects'
import { ViewsPage } from './routes/views'
import { WebhooksPage } from './routes/webhooks'
import { AuditPage } from './routes/audit'
import { SecurityPage } from './routes/security'
import { RouteErrorBoundary } from './components/ErrorBoundary'

export const router = createHashRouter([
  {
    path: '/',
    element: <RootLayout />,
    errorElement: <RouteErrorBoundary />,
    children: [
      {
        index: true,
        element: <Navigate to="/explorer" replace />
      },
      {
        path: 'explorer',
        element: <ExplorerPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'explorer/:tableName',
        element: <ExplorerPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'projects',
        element: <ProjectsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'views',
        element: <ViewsPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'webhooks',
        element: <WebhooksPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'audit',
        element: <AuditPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'security',
        element: <SecurityPage />,
        errorElement: <RouteErrorBoundary />
      },
      {
        path: 'settings',
        element: <SettingsPage />,
        errorElement: <RouteErrorBoundary />
      }
    ]
  }
])
